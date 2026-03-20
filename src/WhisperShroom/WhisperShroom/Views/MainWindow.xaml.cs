using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;
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
        var hWnd = new Windows.Win32.Foundation.HWND(WindowNative.GetWindowHandle(this));
        PInvoke.GetCursorPos(out var cursorPos);

        var hMenu = PInvoke.CreatePopupMenu();
        if (hMenu.IsNull) return;

        try
        {
            PInvoke.AppendMenu(hMenu, MENU_ITEM_FLAGS.MF_STRING,
                MenuId_ToggleRecording, "Aufnahme starten/stoppen");

            PInvoke.AppendMenu(hMenu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, (string?)null);

            var hotkey = App.ConfigService.Config.Hotkey.ToUpperInvariant();
            PInvoke.AppendMenu(hMenu, MENU_ITEM_FLAGS.MF_STRING | MENU_ITEM_FLAGS.MF_GRAYED,
                0, $"Hotkey: {hotkey}");

            PInvoke.AppendMenu(hMenu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, (string?)null);

            // Microphone submenu
            var hMicMenu = PInvoke.CreatePopupMenu();
            var devices = App.AudioService.GetInputDevices();
            var currentDevice = App.ConfigService.Config.DeviceName;

            var defaultFlags = MENU_ITEM_FLAGS.MF_STRING;
            if (currentDevice is null) defaultFlags |= MENU_ITEM_FLAGS.MF_CHECKED;
            PInvoke.AppendMenu(hMicMenu, defaultFlags, MenuId_DefaultDevice, "Standard-Gerät");

            uint deviceId = MenuId_DeviceBase;
            foreach (var device in devices)
            {
                var flags = MENU_ITEM_FLAGS.MF_STRING;
                if (device.Name == currentDevice) flags |= MENU_ITEM_FLAGS.MF_CHECKED;
                PInvoke.AppendMenu(hMicMenu, flags, deviceId, device.Name);
                deviceId++;
            }

            PInvoke.AppendMenu(hMenu, MENU_ITEM_FLAGS.MF_POPUP,
                (nuint)hMicMenu.Value, "Mikrofon");

            PInvoke.AppendMenu(hMenu, MENU_ITEM_FLAGS.MF_STRING,
                MenuId_Settings, "Einstellungen");

            PInvoke.AppendMenu(hMenu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, (string?)null);

            PInvoke.AppendMenu(hMenu, MENU_ITEM_FLAGS.MF_STRING,
                MenuId_Quit, "Beenden");

            // Required so the menu dismisses when clicking outside
            PInvoke.SetForegroundWindow(hWnd);

            var tpmFlags = TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD
                         | TRACK_POPUP_MENU_FLAGS.TPM_BOTTOMALIGN;
            var result = PInvoke.TrackPopupMenuEx(hMenu, (uint)tpmFlags,
                cursorPos.X, cursorPos.Y, hWnd);

            if (result)
            {
                var selectedId = (uint)result.Value;
                DispatcherQueue.TryEnqueue(() => HandleMenuCommand(selectedId, devices));
            }
        }
        finally
        {
            PInvoke.DestroyMenu(hMenu);
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
