using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperShroom.Helpers;
using WhisperShroom.Models;

namespace WhisperShroom.ViewModels;

public partial class SetupWizardViewModel : ObservableObject
{
    [ObservableProperty]
    public partial WizardStep CurrentStep { get; set; }

    [ObservableProperty]
    public partial string ApiKey { get; set; }

    [ObservableProperty]
    public partial string Hotkey { get; set; }

    [ObservableProperty]
    public partial string SelectedDeviceName { get; set; }

    [ObservableProperty]
    public partial List<string> DeviceNames { get; set; }

    [ObservableProperty]
    public partial string SelectedLanguage { get; set; }

    public List<string> AvailableLanguages => LanguageHelper.AvailableLanguages;

    [ObservableProperty]
    public partial string SelectedModel { get; set; }

    [ObservableProperty]
    public partial List<string> AvailableModels { get; set; }

    [ObservableProperty]
    public partial bool AutoCopyEnabled { get; set; }

    [ObservableProperty]
    public partial bool NotificationsEnabled { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial bool IsTesting { get; set; }

    [ObservableProperty]
    public partial string TestResultMessage { get; set; }

    [ObservableProperty]
    public partial bool HasTestResult { get; set; }

    [ObservableProperty]
    public partial bool TestSucceeded { get; set; }

    public bool CanGoNext => CurrentStep != WizardStep.ApiKey || !string.IsNullOrWhiteSpace(ApiKey);
    public bool CanGoBack => CurrentStep > WizardStep.ApiKey;
    public bool IsLastStep => CurrentStep == WizardStep.Features;
    public int StepNumber => (int)CurrentStep + 1;

    public event Action? Completed;

    public SetupWizardViewModel()
    {
        ApiKey = "";
        Hotkey = "ctrl+shift+e";
        AutoCopyEnabled = true;
        NotificationsEnabled = true;
        ErrorMessage = "";
        TestResultMessage = "";

        // Model defaults
        AvailableModels = TranscriptionModelHelper.AllDisplayNames();
        SelectedModel = TranscriptionModelHelper.ToDisplayName(TranscriptionModelHelper.DefaultModelId);

        var devices = App.AudioService.GetInputDevices();
        DeviceNames = ["Default Device", .. devices.Select(d => d.Name)];
        SelectedDeviceName = "Default Device";
        SelectedLanguage = LanguageHelper.ToDisplayName("de");
    }

    partial void OnCurrentStepChanged(WizardStep value)
    {
        HasError = false;
        HasTestResult = false;
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(StepNumber));
    }

    partial void OnApiKeyChanged(string value)
    {
        HasTestResult = false;
        OnPropertyChanged(nameof(CanGoNext));
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            ErrorMessage = "Enter an API key first.";
            HasError = true;
            return;
        }

        HasError = false;
        HasTestResult = false;
        IsTesting = true;

        try
        {
            var trimmedKey = ApiKey.Trim();
            var error = await App.TranscriptionService.ValidateApiKeyAsync(trimmedKey);
            if (error is null)
            {
                TestSucceeded = true;
                TestResultMessage = "Connection successful — transcription API is available.";

                // Fetch available models (separate call — runs only once during setup)
                try
                {
                    var modelIds = await App.TranscriptionService.GetAvailableTranscriptionModelsAsync(trimmedKey);
                    if (modelIds.Count > 0)
                    {
                        AvailableModels = modelIds.Select(TranscriptionModelHelper.ToDisplayName).ToList();
                        SelectedModel = AvailableModels[0];
                    }
                }
                catch
                {
                    // Keep fallback list
                }
            }
            else
            {
                TestSucceeded = false;
                TestResultMessage = error;
            }
            HasTestResult = true;
        }
        catch (Exception ex)
        {
            TestSucceeded = false;
            TestResultMessage = $"Connection failed: {ex.Message}";
            HasTestResult = true;
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private void GoNext()
    {
        HasError = false;

        if (CurrentStep == WizardStep.ApiKey)
        {
            if (string.IsNullOrWhiteSpace(ApiKey))
            {
                ErrorMessage = "API key is required.";
                HasError = true;
                return;
            }
        }

        if (CurrentStep == WizardStep.Hotkey)
        {
            var hk = string.IsNullOrWhiteSpace(Hotkey) ? "ctrl+shift+e" : Hotkey.Trim().ToLowerInvariant();
            try
            {
                Helpers.HotkeyParser.Parse(hk);
            }
            catch (ArgumentException ex)
            {
                ErrorMessage = $"Invalid hotkey: {ex.Message}";
                HasError = true;
                return;
            }
        }

        if (IsLastStep)
        {
            Finish();
            return;
        }

        CurrentStep = (WizardStep)((int)CurrentStep + 1);
    }

    [RelayCommand]
    private void GoBack()
    {
        HasError = false;
        if (CanGoBack)
            CurrentStep = (WizardStep)((int)CurrentStep - 1);
    }

    private void Finish()
    {
        var config = App.ConfigService.Config;
        config.ApiKey = ApiKey.Trim();
        config.Hotkey = string.IsNullOrWhiteSpace(Hotkey) ? "ctrl+shift+e" : Hotkey.Trim().ToLowerInvariant();
        config.DeviceName = SelectedDeviceName == "Default Device" ? null : SelectedDeviceName;
        config.Language = LanguageHelper.ToCode(SelectedLanguage);
        config.Model = TranscriptionModelHelper.ToModelId(SelectedModel);
        config.AutoCopyEnabled = AutoCopyEnabled;
        config.NotificationsEnabled = NotificationsEnabled;

        App.ConfigService.Save();
        Completed?.Invoke();
    }
}
