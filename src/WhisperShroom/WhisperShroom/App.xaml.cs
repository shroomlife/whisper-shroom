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
    public static NotificationService NotificationService { get; } = new();
    public static HistoryService HistoryService { get; } = new();

    public static MainViewModel MainViewModel { get; private set; } = null!;
    public static Views.MainWindow MainAppWindow { get; private set; } = null!;

    private static Views.SetupWizardWindow? _activeWizard;

    public App()
    {
        this.InitializeComponent();
        this.UnhandledException += OnUnhandledException;
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        System.Diagnostics.Debug.WriteLine($"[App] Unhandled XAML exception: {e.Exception}");
    }

    public static void ShowSetupWizard()
    {
        if (_activeWizard is not null)
        {
            _activeWizard.Activate();
            return;
        }

        var wizard = new Views.SetupWizardWindow();
        _activeWizard = wizard;
        wizard.Completed += () =>
        {
            _activeWizard = null;
            MainAppWindow.DispatcherQueue.TryEnqueue(() =>
            {
                HotkeyService.Register(ConfigService.Config.Hotkey, MainViewModel.ToggleRecording);
                MainAppWindow.UpdateTrayTooltip();
                MainAppWindow.Hide();
            });
        };
        wizard.Closed += (_, _) => _activeWizard = null;
        wizard.Activate();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        ConfigService.Load();
        HistoryService.InitializeDatabase();
        NotificationService.Register();
        MainViewModel = new MainViewModel();

        // Window must be created and activated once for WinUI to initialize,
        // then hidden if we want tray-only mode
        // Window must be created and activated for WinUI + tray init.
        // It starts off-screen (see MainWindow.ConfigureWindow) so there's no visible flash.
        MainAppWindow = new Views.MainWindow();
        MainAppWindow.Activate();
        MainAppWindow.Hide();

        if (string.IsNullOrEmpty(ConfigService.Config.ApiKey))
        {
            // First launch: show setup wizard
            ShowSetupWizard();
        }
        else
        {
            // Has config: run from tray only
            HotkeyService.Register(ConfigService.Config.Hotkey, MainViewModel.ToggleRecording);
        }
    }
}
