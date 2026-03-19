using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Win32;
using WinRT.Interop;

namespace WhisperShroom.Views;

public sealed partial class MainWindow : Window
{
    private readonly AppWindow _appWindow;
    private TaskbarIcon? _trayIcon;

    public MainWindow()
    {
        this.InitializeComponent();

        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        ConfigureWindow();
        SetupTrayIcon();
    }

    private void ConfigureWindow()
    {
        _appWindow.Resize(new Windows.Graphics.SizeInt32(560, 420));

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }

        CenterOnScreen();

        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
            if (File.Exists(iconPath))
                _appWindow.SetIcon(iconPath);
        }
        catch { }

        _appWindow.Title = "WhisperShroom";
        _appWindow.Closing += OnClosing;
    }

    private void SetupTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");

        _trayIcon = new TaskbarIcon();

        if (File.Exists(iconPath))
        {
            using var stream = File.OpenRead(iconPath);
            _trayIcon.Icon = new System.Drawing.Icon(stream);
        }

        UpdateTrayTooltip();

        // Double-click: show window
        _trayIcon.DoubleClickCommand = new RelayCommand(() =>
        {
            DispatcherQueue.TryEnqueue(ShowAndActivate);
        });

        // Disable automatic context menu — position it manually at cursor
        _trayIcon.MenuActivation = PopupActivationMode.None;
        _trayIcon.RightClickCommand = new RelayCommand(() =>
        {
            PInvoke.GetCursorPos(out var point);
            _trayIcon.ShowContextMenu(
                new System.Drawing.Point(point.X, point.Y));
        });

        // Context menu via MenuFlyout
        RebuildTrayMenu();

        _trayIcon.ForceCreate();
    }

    private void RebuildTrayMenu()
    {
        if (_trayIcon is null) return;

        var flyout = new MenuFlyout();

        // Start/Stop
        flyout.Items.Add(new MenuFlyoutItem
        {
            Text = "Aufnahme starten/stoppen",
            Command = new RelayCommand(() => App.MainViewModel.ToggleRecording())
        });

        flyout.Items.Add(new MenuFlyoutSeparator());

        // Hotkey display (disabled)
        var hotkey = App.ConfigService.Config.Hotkey.ToUpperInvariant();
        flyout.Items.Add(new MenuFlyoutItem { Text = $"Hotkey: {hotkey}", IsEnabled = false });

        flyout.Items.Add(new MenuFlyoutSeparator());

        // Microphone submenu
        var micSub = new MenuFlyoutSubItem { Text = "Mikrofon" };
        var devices = App.AudioService.GetInputDevices();
        var currentDevice = App.ConfigService.Config.DeviceName;

        micSub.Items.Add(new ToggleMenuFlyoutItem
        {
            Text = "Standard-Gerät",
            IsChecked = currentDevice is null,
            Command = new RelayCommand(() =>
            {
                App.ConfigService.Config.DeviceName = null;
                App.ConfigService.Save();
                RebuildTrayMenu();
            })
        });

        foreach (var device in devices)
        {
            var devName = device.Name;
            micSub.Items.Add(new ToggleMenuFlyoutItem
            {
                Text = devName,
                IsChecked = devName == currentDevice,
                Command = new RelayCommand(() =>
                {
                    App.ConfigService.Config.DeviceName = devName;
                    App.ConfigService.Save();
                    RebuildTrayMenu();
                })
            });
        }

        flyout.Items.Add(micSub);

        // Settings
        flyout.Items.Add(new MenuFlyoutItem
        {
            Text = "Einstellungen",
            Command = new RelayCommand(() =>
            {
                DispatcherQueue.TryEnqueue(() => ShowSettingsWindow());
            })
        });

        flyout.Items.Add(new MenuFlyoutSeparator());

        // Quit
        flyout.Items.Add(new MenuFlyoutItem
        {
            Text = "Beenden",
            Command = new RelayCommand(() =>
            {
                DispatcherQueue.TryEnqueue(() => App.MainViewModel.QuitCommand.Execute(null));
            })
        });

        _trayIcon.ContextFlyout = flyout;
    }

    public void Hide()
    {
        _appWindow.Hide();
    }

    public void UpdateTrayTooltip()
    {
        if (_trayIcon is not null)
        {
            var hotkey = App.ConfigService.Config.Hotkey.ToUpperInvariant();
            _trayIcon.ToolTipText = $"WhisperShroom ({hotkey})";
        }
    }

    private void CenterOnScreen()
    {
        var displayArea = DisplayArea.GetFromWindowId(
            _appWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;

        var x = (workArea.Width - 560) / 2 + workArea.X;
        var y = (workArea.Height - 420) / 2 + workArea.Y;
        _appWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        args.Cancel = true;
        _appWindow.Hide();

        if (App.MainViewModel.CurrentState == Models.AppState.Recording)
        {
            App.MainViewModel.CancelRecordingCommand.Execute(null);
        }
    }

    public void ShowAndActivate()
    {
        _appWindow.Show();
        this.Activate();
        CenterOnScreen();
    }

    public void ForceClose()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        _appWindow.Closing -= OnClosing;
        this.Close();
    }

    public void ShowSettingsWindow()
    {
        var settingsWindow = new SettingsWindow();
        settingsWindow.Saved += () =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateTrayTooltip();
                RebuildTrayMenu();
            });
        };
        settingsWindow.Activate();
    }
}
