using Microsoft.UI.Xaml;
using WhisperShroom.Services;
using WhisperShroom.ViewModels;

namespace WhisperShroom;

public partial class App : Application
{
    public static ConfigService ConfigService { get; } = new();
    public static AudioService AudioService { get; } = new();
    public static TranscriptionService TranscriptionService { get; } = new();
    public static HotkeyService HotkeyService { get; } = new();

    public static MainViewModel MainViewModel { get; private set; } = null!;
    public static Views.MainWindow MainAppWindow { get; private set; } = null!;

    public App()
    {
        this.InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        ConfigService.Load();
        MainViewModel = new MainViewModel();

        // Window must be created and activated once for WinUI to initialize,
        // then hidden if we want tray-only mode
        MainAppWindow = new Views.MainWindow();
        MainAppWindow.Activate();

        if (string.IsNullOrEmpty(ConfigService.Config.ApiKey))
        {
            // First launch: show settings
            MainAppWindow.ShowSettingsWindow();
        }
        else
        {
            // Has config: hide window, run from tray only
            HotkeyService.Register(ConfigService.Config.Hotkey, MainViewModel.ToggleRecording);
            // Hide window after tray icon is initialized - run from tray only
            MainAppWindow.Hide();
        }
    }
}
