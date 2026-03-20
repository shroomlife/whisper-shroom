using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WhisperShroom.Helpers;
using WinRT.Interop;

namespace WhisperShroom.Views;

public sealed partial class MainWindow : Window
{
    private readonly AppWindow _appWindow;
    private TaskbarIcon? _trayIcon;

    private const uint MenuId_ToggleRecording = 1;
    private const uint MenuId_Settings = 2;
    private const uint MenuId_Quit = 3;
    private const uint MenuId_DefaultDevice = 100;
    private const uint MenuId_DeviceBase = 101;

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

        _trayIcon = new TaskbarIcon
        {
            // Stable GUID so Windows remembers tray icon visibility across MSIX updates
            Id = new Guid("A1B2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5D")
        };

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

        // Disable H.NotifyIcon's built-in context menu — use native Win32 popup menu instead
        // (H.NotifyIcon's PopupMenu mode needs a separate NuGet package, SecondWindow has sizing bugs)
        _trayIcon.MenuActivation = PopupActivationMode.None;
        _trayIcon.RightClickCommand = new RelayCommand(ShowNativeContextMenu);

        _trayIcon.ForceCreate();
    }

    private void ShowNativeContextMenu()
    {
        var hWnd = WindowNative.GetWindowHandle(this);
        NativeMenu.GetCursorPos(out var cursorPos);

        var hMenu = NativeMenu.CreatePopupMenu();
        if (hMenu == 0) return;

        try
        {
            NativeMenu.AppendMenuW(hMenu, NativeMenu.MF_STRING,
                MenuId_ToggleRecording, "Aufnahme starten/stoppen");

            NativeMenu.AppendMenuW(hMenu, NativeMenu.MF_SEPARATOR, 0, null);

            var hotkey = App.ConfigService.Config.Hotkey.ToUpperInvariant();
            NativeMenu.AppendMenuW(hMenu, NativeMenu.MF_STRING | NativeMenu.MF_GRAYED,
                0, $"Hotkey: {hotkey}");

            NativeMenu.AppendMenuW(hMenu, NativeMenu.MF_SEPARATOR, 0, null);

            // Microphone submenu
            var hMicMenu = NativeMenu.CreatePopupMenu();
            var devices = App.AudioService.GetInputDevices();
            var currentDevice = App.ConfigService.Config.DeviceName;

            var defaultFlags = NativeMenu.MF_STRING;
            if (currentDevice is null) defaultFlags |= NativeMenu.MF_CHECKED;
            NativeMenu.AppendMenuW(hMicMenu, defaultFlags, MenuId_DefaultDevice, "Standard-Gerät");

            uint deviceId = MenuId_DeviceBase;
            foreach (var device in devices)
            {
                var flags = NativeMenu.MF_STRING;
                if (device.Name == currentDevice) flags |= NativeMenu.MF_CHECKED;
                NativeMenu.AppendMenuW(hMicMenu, flags, deviceId, device.Name);
                deviceId++;
            }

            NativeMenu.AppendMenuW(hMenu, NativeMenu.MF_POPUP,
                (nuint)hMicMenu, "Mikrofon");

            NativeMenu.AppendMenuW(hMenu, NativeMenu.MF_STRING,
                MenuId_Settings, "Einstellungen");

            NativeMenu.AppendMenuW(hMenu, NativeMenu.MF_SEPARATOR, 0, null);

            NativeMenu.AppendMenuW(hMenu, NativeMenu.MF_STRING,
                MenuId_Quit, "Beenden");

            // Required so the menu dismisses when clicking outside
            NativeMenu.SetForegroundWindow(hWnd);

            var cmd = NativeMenu.TrackPopupMenuEx(hMenu,
                NativeMenu.TPM_RETURNCMD | NativeMenu.TPM_BOTTOMALIGN,
                cursorPos.X, cursorPos.Y, hWnd, 0);

            if (cmd != 0)
            {
                DispatcherQueue.TryEnqueue(() => HandleMenuCommand((uint)cmd, devices));
            }
        }
        finally
        {
            NativeMenu.DestroyMenu(hMenu);
        }
    }

    private void HandleMenuCommand(uint commandId, IReadOnlyList<Models.AudioDevice> devices)
    {
        switch (commandId)
        {
            case MenuId_ToggleRecording:
                App.MainViewModel.ToggleRecording();
                break;
            case MenuId_Settings:
                ShowSettingsWindow();
                break;
            case MenuId_Quit:
                App.MainViewModel.QuitCommand.Execute(null);
                break;
            case MenuId_DefaultDevice:
                App.ConfigService.Config.DeviceName = null;
                App.ConfigService.Save();
                break;
            default:
                if (commandId >= MenuId_DeviceBase)
                {
                    var index = (int)(commandId - MenuId_DeviceBase);
                    if (index < devices.Count)
                    {
                        App.ConfigService.Config.DeviceName = devices[index].Name;
                        App.ConfigService.Save();
                    }
                }
                break;
        }
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
            DispatcherQueue.TryEnqueue(UpdateTrayTooltip);
        };
        settingsWindow.Activate();
    }
}
