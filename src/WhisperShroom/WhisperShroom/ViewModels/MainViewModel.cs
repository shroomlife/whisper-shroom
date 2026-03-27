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
        App.AudioService.RmsUpdated += OnRmsUpdated;
    }

    public void ToggleRecording()
    {
        _dispatcher.TryEnqueue(() =>
        {
            if (CurrentState == AppState.Recording)
                StopRecording();
            else
                StartRecording();
        });
    }

    [RelayCommand]
    private void StartRecording()
    {
        if (CurrentState == AppState.Recording) return;

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
            string? deviceId = null;

            if (config.DeviceName is not null)
            {
                var devices = App.AudioService.GetInputDevices();
                var match = devices.FirstOrDefault(d => d.Name == config.DeviceName);
                deviceId = match?.Id;
            }

            App.AudioService.StartRecording(deviceId);
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
        var wavData = App.AudioService.StopRecording();

        if (wavData is null || wavData.Length == 0)
        {
            CurrentState = AppState.Ready;
            return;
        }

        CurrentState = AppState.Loading;

        try
        {
            var apiKey = App.ConfigService.Config.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                ShowSettingsOnError = true;
                ErrorMessage = "No API key configured. Please add your OpenAI API key in Settings.";
                CurrentState = AppState.Error;
                return;
            }

            var config = App.ConfigService.Config;
            var language = config.Language;
            var model = config.Model ?? TranscriptionModelHelper.DefaultModelId;
            var result = await Task.Run(() =>
                App.TranscriptionService.TranscribeAsync(wavData, apiKey, language, model));

            var trimmed = result.Text.Trim();

            if (HallucinationFilter.IsHallucination(trimmed))
            {
                ErrorMessage = "No audio detected. Please check your microphone in Settings.";
                CurrentState = AppState.Error;
                return;
            }

            ResultText = trimmed;

            var entryResult = result with { Text = trimmed };
            App.HistoryService.AddEntry(entryResult, model, language);
            HasPrefix = !string.IsNullOrEmpty(config.PromptPrefix);
            HasSuffix = !string.IsNullOrEmpty(config.PromptSuffix);
            IncludePrefix = true;
            IncludeSuffix = true;
            CurrentState = AppState.Result;

            if (config.AutoCopyEnabled)
            {
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
                ClipboardHelper.CopyToClipboard(AssembleCopyText(), hWnd);
            }

            if (config.NotificationsEnabled)
            {
                App.NotificationService.ShowTranscriptionResult(trimmed);
            }
        }
        catch (Exception ex)
        {
            ShowSettingsOnError = ex.Message.Contains("API key", StringComparison.OrdinalIgnoreCase);
            ErrorMessage = ex.Message;
            CurrentState = AppState.Error;
        }
    }

    private void StopRecording()
    {
        _ = StopRecordingAsync();
    }

    [RelayCommand]
    private void CancelRecording()
    {
        StopTimer();
        App.AudioService.CancelRecording();
        CurrentState = AppState.Ready;
    }

    [RelayCommand]
    private void NewRecording()
    {
        ShowSettingsOnError = false;
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

    private void OnRmsUpdated(float rms)
    {
        // RMS updates come from the audio thread, handled via timer on UI thread
    }

    private void StopTimer()
    {
        _timer?.Stop();
        _timer = null;
        _recordingStopwatch.Stop();
    }
}
