using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    public partial bool AutoCopyEnabled { get; set; }

    [ObservableProperty]
    public partial bool NotificationsEnabled { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

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

        var devices = App.AudioService.GetInputDevices();
        DeviceNames = ["Default Device", .. devices.Select(d => d.Name)];
        SelectedDeviceName = "Default Device";
    }

    partial void OnCurrentStepChanged(WizardStep value)
    {
        HasError = false;
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(StepNumber));
    }

    partial void OnApiKeyChanged(string value)
    {
        OnPropertyChanged(nameof(CanGoNext));
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
        config.AutoCopyEnabled = AutoCopyEnabled;
        config.NotificationsEnabled = NotificationsEnabled;

        App.ConfigService.Save();
        Completed?.Invoke();
    }
}
