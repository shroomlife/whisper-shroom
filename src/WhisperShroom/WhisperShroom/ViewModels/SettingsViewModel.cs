using CommunityToolkit.Mvvm.ComponentModel;
using WhisperShroom.Models;

namespace WhisperShroom.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string ApiKey { get; set; }

    [ObservableProperty]
    public partial string Hotkey { get; set; }

    [ObservableProperty]
    public partial string SelectedDeviceName { get; set; }

    [ObservableProperty]
    public partial List<string> DeviceNames { get; set; }

    public SettingsViewModel()
    {
        var config = App.ConfigService.Config;
        ApiKey = config.ApiKey ?? "";
        Hotkey = config.Hotkey;

        // Load devices
        var devices = App.AudioService.GetInputDevices();
        DeviceNames = ["Default Device", .. devices.Select(d => d.Name)];

        SelectedDeviceName = config.DeviceName is not null && DeviceNames.Contains(config.DeviceName)
            ? config.DeviceName
            : "Default Device";
    }

    public bool Save()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            return false;

        // Validate hotkey
        var hk = string.IsNullOrWhiteSpace(Hotkey) ? "ctrl+shift+e" : Hotkey.Trim().ToLowerInvariant();
        try
        {
            Helpers.HotkeyParser.Parse(hk);
        }
        catch
        {
            return false;
        }

        var config = App.ConfigService.Config;
        config.ApiKey = ApiKey.Trim();
        config.Hotkey = hk;
        config.DeviceName = SelectedDeviceName == "Default Device" ? null : SelectedDeviceName;

        App.ConfigService.Save();

        // Re-register hotkey
        App.HotkeyService.Register(config.Hotkey, App.MainViewModel.ToggleRecording);
        App.MainViewModel.RefreshHotkeyDisplay();

        return true;
    }
}
