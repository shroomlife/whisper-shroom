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

        try
        {
            var config = App.ConfigService.Config;
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
            if (string.IsNullOrEmpty(apiKey))
            {
                ErrorMessage = "API key not configured.";
                CurrentState = AppState.Error;
                return;
            }

            var language = App.ConfigService.Config.Language;
            var text = await Task.Run(() =>
                App.TranscriptionService.TranscribeAsync(wavData, apiKey, language));

            var trimmed = text.Trim();

            if (HallucinationFilter.IsHallucination(trimmed))
            {
                ErrorMessage = "No audio detected. Please check your microphone in Settings.";
                CurrentState = AppState.Error;
                return;
            }

            ResultText = trimmed;
            CurrentState = AppState.Result;

            var config = App.ConfigService.Config;

            if (config.AutoCopyEnabled)
            {
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
                ClipboardHelper.CopyToClipboard(trimmed, hWnd);
            }

            if (config.NotificationsEnabled)
            {
                App.NotificationService.ShowTranscriptionResult(trimmed);
            }
        }
        catch (Exception ex)
        {
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
        CurrentState = AppState.Ready;
    }

    [RelayCommand]
    private void CopyText()
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
        ClipboardHelper.CopyToClipboard(ResultText, hWnd);
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
