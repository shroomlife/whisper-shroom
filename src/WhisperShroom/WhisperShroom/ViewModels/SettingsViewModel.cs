using CommunityToolkit.Mvvm.ComponentModel;
using WhisperShroom.Helpers;
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

    [ObservableProperty]
    public partial string SelectedLanguage { get; set; }

    public List<string> AvailableLanguages => LanguageHelper.AvailableLanguages;

    [ObservableProperty]
    public partial string SelectedModel { get; set; }

    [ObservableProperty]
    public partial List<string> AvailableModels { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingModels { get; set; }

    [ObservableProperty]
    public partial bool AutoCopyEnabled { get; set; }

    [ObservableProperty]
    public partial bool NotificationsEnabled { get; set; }

    public string Prefix { get; set; }

    public string Suffix { get; set; }

    [ObservableProperty]
    public partial string PrefixPreview { get; set; }

    [ObservableProperty]
    public partial string SuffixPreview { get; set; }

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

        SelectedLanguage = LanguageHelper.ToDisplayName(config.Language);

        // Model: show current selection, start with fallback list
        var currentModelId = config.Model ?? TranscriptionModelHelper.DefaultModelId;
        AvailableModels = TranscriptionModelHelper.AllDisplayNames();
        SelectedModel = TranscriptionModelHelper.ToDisplayName(currentModelId);

        AutoCopyEnabled = config.AutoCopyEnabled;
        NotificationsEnabled = config.NotificationsEnabled;

        // Prefix / Suffix
        Prefix = config.PromptPrefix ?? "";
        Suffix = config.PromptSuffix ?? "";
        PrefixPreview = FormatPreview(Prefix);
        SuffixPreview = FormatPreview(Suffix);
    }

    /// <summary>
    /// Fetches available transcription models from the OpenAI API.
    /// Falls back to the full known list on failure.
    /// </summary>
    public async Task LoadModelsAsync()
    {
        var apiKey = ApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
            return;

        IsLoadingModels = true;
        try
        {
            var modelIds = await App.TranscriptionService.GetAvailableTranscriptionModelsAsync(apiKey);
            if (modelIds.Count > 0)
            {
                var currentSelection = SelectedModel;
                AvailableModels = modelIds.Select(TranscriptionModelHelper.ToDisplayName).ToList();

                // Preserve current selection if still available, otherwise default
                SelectedModel = AvailableModels.Contains(currentSelection)
                    ? currentSelection
                    : AvailableModels.First();
            }
        }
        catch
        {
            // Keep fallback list on failure
        }
        finally
        {
            IsLoadingModels = false;
        }
    }

    public void UpdatePrefix(string value)
    {
        Prefix = value;
        PrefixPreview = FormatPreview(value);
    }

    public void UpdateSuffix(string value)
    {
        Suffix = value;
        SuffixPreview = FormatPreview(value);
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
        config.Language = LanguageHelper.ToCode(SelectedLanguage);
        config.Model = TranscriptionModelHelper.ToModelId(SelectedModel);
        config.AutoCopyEnabled = AutoCopyEnabled;
        config.NotificationsEnabled = NotificationsEnabled;
        config.PromptPrefix = string.IsNullOrEmpty(Prefix) ? null : Prefix;
        config.PromptSuffix = string.IsNullOrEmpty(Suffix) ? null : Suffix;

        App.ConfigService.Save();

        // Re-register hotkey
        App.HotkeyService.Register(config.Hotkey, App.MainViewModel.ToggleRecording);
        App.MainViewModel.RefreshHotkeyDisplay();

        return true;
    }

    private static string FormatPreview(string value) =>
        string.IsNullOrEmpty(value)
            ? "(none)"
            : value.Length <= 40
                ? value.ReplaceLineEndings(" ")
                : $"{value[..37].ReplaceLineEndings(" ")}...";
}
