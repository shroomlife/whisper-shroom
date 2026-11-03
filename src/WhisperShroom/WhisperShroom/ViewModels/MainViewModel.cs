using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using WhisperShroom.Helpers;
using WhisperShroom.Models;
using WhisperShroom.Services;

namespace WhisperShroom.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DispatcherQueue _dispatcher;
    private readonly Stopwatch _recordingStopwatch = new();
    private DispatcherQueueTimer? _timer;
    private bool _silenceWarned;

    // Tracks the currently persisted (pending) recording so the Retry command can
    // re-transcribe the exact same audio. Cleared on success / new recording.
    private string? _pendingEntryId;
    private string? _pendingAudioPath;
    private string? _pendingModel;
    private string? _pendingLanguage;

    [ObservableProperty]
    public partial AppState CurrentState { get; set; }

    [ObservableProperty]
    public partial string RecordingTime { get; set; }

    [ObservableProperty]
    public partial string ResultText { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool ShowSilenceWarning { get; set; }

    [ObservableProperty]
    public partial bool ShowSettingsOnError { get; set; }

    [ObservableProperty]
    public partial bool HasPrefix { get; set; }

    [ObservableProperty]
    public partial bool HasSuffix { get; set; }

    [ObservableProperty]
    public partial bool IncludePrefix { get; set; } = true;

    [ObservableProperty]
    public partial bool IncludeSuffix { get; set; } = true;

    public string HotkeyDisplay => App.ConfigService.Config.Hotkey.ToUpperInvariant();

    public MainViewModel()
    {
        CurrentState = AppState.Ready;
        RecordingTime = "00:00";
        ResultText = "";
        ErrorMessage = "";

        _dispatcher = DispatcherQueue.GetForCurrentThread();
    }

    /// <summary>
    /// Hotkey / tray entry point (may be called from the hotkey thread). While a recording is
    /// being transcribed the toggle is ignored, so a second recording can never overwrite the
    /// pending entry the running transcription will resolve.
    /// </summary>
    public void ToggleRecording()
    {
        _dispatcher.TryEnqueue(() =>
        {
            switch (CurrentState)
            {
                case AppState.Recording:
                    StopRecording();
                    break;
                case AppState.Loading:
                    break;
                default:
                    StartRecording();
                    break;
            }
        });
    }

    [RelayCommand]
    private void StartRecording()
    {
        // IsBusy: a cancelled recording may still be finishing on NAudio's capture thread.
        if (CurrentState is AppState.Recording or AppState.Loading || App.AudioService.IsBusy) return;

        var config = App.ConfigService.Config;

        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            ShowSettingsOnError = true;
            ErrorMessage = "No API key configured. Please add your OpenAI API key in Settings.";
            CurrentState = AppState.Error;
            App.MainAppWindow.ShowAndActivate();
            return;
        }

        try
        {
            var deviceId = config.DeviceName is not null
                ? App.AudioService.FindDeviceId(config.DeviceName)
                : null;

            var audioPath = Path.Combine(App.HistoryService.AudioDir, $"{Guid.NewGuid()}.wav");
            App.AudioService.StartRecording(deviceId, audioPath);
            _silenceWarned = false;
            ShowSilenceWarning = false;
            _recordingStopwatch.Restart();
            RecordingTime = "00:00";
            CurrentState = AppState.Recording;

            // Show the main window
            App.MainAppWindow.ShowAndActivate();

            // Start UI timer
            _timer = _dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(200);
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            CurrentState = AppState.Error;
            App.MainAppWindow.ShowAndActivate();
        }
    }

    [RelayCommand]
    private async Task StopRecordingAsync()
    {
        if (CurrentState != AppState.Recording) return;

        StopTimer();

        // Loading from here on: the hotkey toggle ignores input until the state is resolved.
        CurrentState = AppState.Loading;

        RecordingResult? recording;
        try
        {
            recording = await App.AudioService.StopRecordingAsync();
        }
        catch (Exception ex)
        {
            ShowSettingsOnError = false;
            ErrorMessage = $"Could not finish recording: {ex.Message}";
            CurrentState = AppState.Error;
            return;
        }

        if (recording is null)
        {
            CurrentState = AppState.Ready;
            return;
        }

        var config = App.ConfigService.Config;
        var language = config.Language;
        var model = config.Model ?? TranscriptionModelHelper.DefaultModelId;

        // The WAV is already finalized on disk; register it as a pending entry before any
        // API call so nothing is lost. Wrapped in try/catch because the async RelayCommand
        // default is "await and rethrow", which would otherwise crash the app on a DB error.
        try
        {
            _pendingEntryId = App.HistoryService.AddPendingEntry(recording.FilePath, model, language, "");
            _pendingAudioPath = recording.FilePath;
            _pendingModel = model;
            _pendingLanguage = language;
        }
        catch (Exception ex)
        {
            _pendingEntryId = null;
            _pendingAudioPath = null;
            ShowSettingsOnError = false;
            ErrorMessage = $"Could not save recording: {ex.Message}";
            CurrentState = AppState.Error;
            return;
        }

        // The capture device failed mid-recording (e.g. unplugged). What was captured up to
        // that point is kept and can be retried.
        if (recording.Error is not null)
        {
            var message = $"Recording stopped unexpectedly: {recording.Error.Message}";
            App.HistoryService.UpdatePendingError(_pendingEntryId, message);
            ShowSettingsOnError = false;
            ErrorMessage = message;
            CurrentState = AppState.Error;
            return;
        }

        // Skip the (billed) API call for truly silent recordings, but keep them persisted
        // so the user can still force a retry from the error panel or History.
        if (!recording.HasAudio)
        {
            App.HistoryService.UpdatePendingError(_pendingEntryId!, TranscriptionWorkflow.NoSpeechMessage);
            ShowSettingsOnError = false;
            ErrorMessage = TranscriptionWorkflow.NoSpeechMessage;
            CurrentState = AppState.Error;
            return;
        }

        await TranscribeCurrentPendingAsync();
    }

    /// <summary>
    /// Transcribes the currently persisted pending recording and drives the UI state.
    /// Shared by the initial stop flow and the Retry command.
    /// </summary>
    private async Task TranscribeCurrentPendingAsync()
    {
        if (_pendingEntryId is null || _pendingAudioPath is null) return;

        CurrentState = AppState.Loading;

        var config = App.ConfigService.Config;
        var apiKey = config.ApiKey;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            App.HistoryService.UpdatePendingError(
                _pendingEntryId, "No API key configured. Please add your OpenAI API key in Settings.");
            ShowSettingsOnError = true;
            ErrorMessage = "No API key configured. Please add your OpenAI API key in Settings.";
            CurrentState = AppState.Error;
            return;
        }

        var model = _pendingModel ?? config.Model ?? TranscriptionModelHelper.DefaultModelId;
        var outcome = await TranscriptionWorkflow.RunPendingAsync(
            _pendingEntryId, _pendingAudioPath, apiKey, _pendingLanguage, model);

        if (outcome.Kind == TranscriptionWorkflow.OutcomeKind.Success && outcome.Result is not null)
        {
            ResultText = outcome.Result.Text;
            HasPrefix = !string.IsNullOrEmpty(config.PromptPrefix);
            HasSuffix = !string.IsNullOrEmpty(config.PromptSuffix);
            IncludePrefix = true;
            IncludeSuffix = true;
            ShowSettingsOnError = false;
            CurrentState = AppState.Result;

            ClearPending();

            if (config.AutoCopyEnabled)
            {
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
                ClipboardHelper.CopyToClipboard(AssembleCopyText(), hWnd);
            }

            if (config.NotificationsEnabled)
            {
                App.NotificationService.ShowTranscriptionResult(outcome.Result.Text);
            }

            return;
        }

        // Hallucination or failure: keep _pending* so the Retry command can try again.
        var message = outcome.ErrorMessage ?? "Transcription failed.";
        ShowSettingsOnError = message.Contains("API key", StringComparison.OrdinalIgnoreCase);
        ErrorMessage = message;
        CurrentState = AppState.Error;
    }

    [RelayCommand]
    private async Task RetryAsync()
    {
        if (_pendingEntryId is null || _pendingAudioPath is null)
        {
            // Nothing persisted to retry — fall back to a fresh state.
            ShowSettingsOnError = false;
            CurrentState = AppState.Ready;
            return;
        }

        await TranscribeCurrentPendingAsync();
    }

    private void ClearPending()
    {
        _pendingEntryId = null;
        _pendingAudioPath = null;
        _pendingModel = null;
        _pendingLanguage = null;
    }

    private void StopRecording()
    {
        _ = StopRecordingAsync();
    }

    [RelayCommand]
    private async Task CancelRecordingAsync()
    {
        if (CurrentState != AppState.Recording) return;

        StopTimer();
        CurrentState = AppState.Ready;

        try
        {
            await App.AudioService.CancelRecordingAsync();
        }
        catch (Exception ex)
        {
            ShowSettingsOnError = false;
            ErrorMessage = $"Could not cancel recording: {ex.Message}";
            CurrentState = AppState.Error;
        }
    }

    [RelayCommand]
    private void NewRecording()
    {
        ShowSettingsOnError = false;
        ClearPending();
        CurrentState = AppState.Ready;
    }

    [RelayCommand]
    private void CopyText()
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
        ClipboardHelper.CopyToClipboard(AssembleCopyText(), hWnd);
    }

    private string AssembleCopyText()
    {
        var config = App.ConfigService.Config;
        var parts = new List<string>();

        if (IncludePrefix && !string.IsNullOrEmpty(config.PromptPrefix))
            parts.Add(config.PromptPrefix);

        parts.Add(ResultText);

        if (IncludeSuffix && !string.IsNullOrEmpty(config.PromptSuffix))
            parts.Add(config.PromptSuffix);

        return string.Join(" ", parts);
    }

    [RelayCommand]
    private void OpenSettings()
    {
        App.MainAppWindow.ShowSettingsWindow();
    }

    [RelayCommand]
    private void Quit()
    {
        App.HotkeyService.Dispose();
        App.AudioService.Dispose();
        App.TranscriptionService.Dispose();
        App.NotificationService.Dispose();
        App.MainAppWindow.ForceClose();
    }

    public void RefreshHotkeyDisplay()
    {
        OnPropertyChanged(nameof(HotkeyDisplay));
    }

    private void OnTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (CurrentState != AppState.Recording)
        {
            StopTimer();
            return;
        }

        var elapsed = _recordingStopwatch.Elapsed;
        RecordingTime = $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";

        // Silence warning after 5 seconds
        if (elapsed.TotalSeconds >= 5 && !App.AudioService.HasAudio && !_silenceWarned)
        {
            _silenceWarned = true;
            ShowSilenceWarning = true;
        }
        else if (App.AudioService.HasAudio && _silenceWarned)
        {
            ShowSilenceWarning = false;
        }
    }

    private void StopTimer()
    {
        _timer?.Stop();
        _timer = null;
        _recordingStopwatch.Stop();
    }
}
