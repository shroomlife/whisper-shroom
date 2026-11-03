using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WhisperShroom.Helpers;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;
using WinRT.Interop;

namespace WhisperShroom.Views;

public sealed partial class MainWindow : Window
{
    private readonly AppWindow _appWindow;
    private TaskbarIcon? _trayIcon;

    // Tray icons per state, loaded once. The tray icon is plain Win32 (Shell_NotifyIcon), so
    // showing state there does not expose a XAML window to UI Automation clients.
    private System.Drawing.Icon? _iconReady;
    private System.Drawing.Icon? _iconRecording;
    private System.Drawing.Icon? _iconTranscribing;
    private System.Drawing.Icon? _iconDone;
    private System.Drawing.Icon? _iconError;
    private Models.AppState _trayState = Models.AppState.Ready;

    // After a transcription the tray briefly shows its outcome (green success, red error)
    // before returning to the normal icon, so tray mode gives visible feedback even with
    // notifications turned off.
    private static readonly TimeSpan OutcomeBadgeDuration = TimeSpan.FromSeconds(5);
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _outcomeBadgeTimer;

    private const int WindowWidth = 600;
    private const int WindowHeight = 500;

    private const uint MenuId_ToggleRecording = 1;
    private const uint MenuId_Settings = 2;
    private const uint MenuId_Quit = 3;
    private const uint MenuId_History = 4;
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
        _appWindow.Resize(new Windows.Graphics.SizeInt32(WindowWidth, WindowHeight));

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }

        // Start off-screen to prevent visible flash during Activate().
        // CenterOnScreen() is called by ShowAndActivate() when the window needs to be shown.
        _appWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));

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

    /// <summary>
    /// Loads the image of an .ico that matches the notification area's icon size. Icon(Stream)
    /// alone returns the smallest image (16 px), which the shell would upscale on scaled
    /// displays. The tray uses the system small icon size (SM_CXSMICON: 16/20/24/32/40 px at
    /// 100-250 %), queried for the system DPI, i.e. the primary monitor where the
    /// notification area lives. The .ico files contain all of these sizes.
    /// </summary>
    private static System.Drawing.Icon? LoadTrayIcon(string fileName, int size)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
        if (!File.Exists(path))
            return null;

        using var stream = File.OpenRead(path);
        return new System.Drawing.Icon(stream, size, size);
    }

    private static int GetTrayIconSize()
    {
        var size = PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSMICON, PInvoke.GetDpiForSystem());
        return size > 0 ? size : 16;
    }

    private void SetupTrayIcon()
    {
        var size = GetTrayIconSize();
        _iconReady = LoadTrayIcon("icon-tray.ico", size);
        _iconRecording = LoadTrayIcon("icon-recording.ico", size);
        _iconTranscribing = LoadTrayIcon("icon-transcribing.ico", size);
        _iconDone = LoadTrayIcon("icon-done.ico", size);
        _iconError = LoadTrayIcon("icon-error.ico", size);

        _outcomeBadgeTimer = DispatcherQueue.CreateTimer();
        _outcomeBadgeTimer.Interval = OutcomeBadgeDuration;
        _outcomeBadgeTimer.IsRepeating = false;
        _outcomeBadgeTimer.Tick += (_, _) =>
        {
            if (_trayState is Models.AppState.Result or Models.AppState.Error && _iconReady is not null)
                _trayIcon?.UpdateIcon(_iconReady);
        };

        _trayIcon = new TaskbarIcon
        {
            // Stable GUID so Windows remembers tray icon visibility across MSIX updates
            Id = new Guid("A1B2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5D")
        };

        if (_iconReady is not null)
            _trayIcon.Icon = _iconReady;

        UpdateTrayTooltip();

        // Left-click: toggle recording (like pressing the hotkey), or open wizard if no API key
        _trayIcon.LeftClickCommand = new RelayCommand(() =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (string.IsNullOrWhiteSpace(App.ConfigService.Config.ApiKey))
                    App.ShowSetupWizard();
                else
                    App.MainViewModel.ToggleRecording();
            });
        });

        // Double-click: show window
        _trayIcon.DoubleClickCommand = new RelayCommand(() =>
        {
            DispatcherQueue.TryEnqueue(ShowAndActivate);
        });

        // Disable H.NotifyIcon's built-in context menu — use native Win32 popup menu instead
        // (H.NotifyIcon's PopupMenu mode needs a separate NuGet package, SecondWindow has sizing bugs)
        _trayIcon.MenuActivation = PopupActivationMode.None;
        _trayIcon.RightClickCommand = new RelayCommand(ShowNativeContextMenu);

        // ForceCreate() defaults to enablesEfficiencyMode: true, which puts the whole process
        // into IDLE_PRIORITY_CLASS plus EcoQoS for its lifetime, including while recording and
        // uploading. Microsoft's guidance is that EcoQoS must not be used for foreground or
        // performance-critical work; without an explicit opt-in Windows derives the QoS from
        // window visibility on its own, which already lowers it while the app sits in the tray.
        _trayIcon.ForceCreate(enablesEfficiencyMode: false);
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
                MenuId_ToggleRecording, "Toggle Recording");

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
            NativeMenu.AppendMenuW(hMicMenu, defaultFlags, MenuId_DefaultDevice, "Default Device");

            uint deviceId = MenuId_DeviceBase;
            foreach (var device in devices)
            {
                var flags = NativeMenu.MF_STRING;
                if (device.Name == currentDevice) flags |= NativeMenu.MF_CHECKED;
                NativeMenu.AppendMenuW(hMicMenu, flags, deviceId, device.Name);
                deviceId++;
            }

            NativeMenu.AppendMenuW(hMenu, NativeMenu.MF_POPUP,
                (nuint)hMicMenu, "Microphone");

            NativeMenu.AppendMenuW(hMenu, NativeMenu.MF_STRING,
                MenuId_Settings, "Settings");

            NativeMenu.AppendMenuW(hMenu, NativeMenu.MF_STRING,
                MenuId_History, "History");

            NativeMenu.AppendMenuW(hMenu, NativeMenu.MF_SEPARATOR, 0, null);

            NativeMenu.AppendMenuW(hMenu, NativeMenu.MF_STRING,
                MenuId_Quit, "Quit");

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
                if (string.IsNullOrWhiteSpace(App.ConfigService.Config.ApiKey))
                    App.ShowSetupWizard();
                else
                    App.MainViewModel.ToggleRecording();
                break;
            case MenuId_Settings:
                ShowSettingsWindow();
                break;
            case MenuId_History:
                ShowHistoryWindow();
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
        RootPage.SetWindowVisible(false);
    }

    /// <summary>Reflects the recording state on the tray icon and its tooltip.</summary>
    public void SetTrayState(Models.AppState state)
    {
        _trayState = state;

        if (_trayIcon is null)
            return;

        _outcomeBadgeTimer?.Stop();

        // Red dot while recording, yellow while transcribing; then green (success) or the
        // standard red error overlay with a white x
        // for OutcomeBadgeDuration before the normal icon returns.
        var icon = state switch
        {
            Models.AppState.Recording => _iconRecording,
            Models.AppState.Loading => _iconTranscribing,
            Models.AppState.Result => _iconDone,
            Models.AppState.Error => _iconError,
            _ => _iconReady
        };

        if (state is Models.AppState.Result or Models.AppState.Error)
            _outcomeBadgeTimer?.Start();

        // UpdateIcon swaps the shell icon without touching the Icon property. Assigning the
        // property instead would make H.NotifyIcon dispose the previous value
        // (OnIconChanged: oldValue?.Dispose()), destroying the cached icons this window reuses.
        if (icon is not null)
            _trayIcon.UpdateIcon(icon);

        UpdateTrayTooltip();
    }

    public void UpdateTrayTooltip()
    {
        if (_trayIcon is null)
            return;

        var hotkey = App.ConfigService.Config.Hotkey.ToUpperInvariant();
        _trayIcon.ToolTipText = _trayState switch
        {
            Models.AppState.Recording => $"WhisperShroom: Recording... ({hotkey} to stop)",
            Models.AppState.Loading => "WhisperShroom: Transcribing...",
            Models.AppState.Result when App.ConfigService.Config.AutoCopyEnabled =>
                $"WhisperShroom: Done, copied to clipboard ({hotkey})",
            Models.AppState.Result => $"WhisperShroom: Done ({hotkey})",
            Models.AppState.Error => $"WhisperShroom: Failed, see window ({hotkey})",
            _ => $"WhisperShroom ({hotkey})"
        };
    }

    private void CenterOnScreen()
    {
        var displayArea = DisplayArea.GetFromWindowId(
            _appWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;

        var x = (workArea.Width - WindowWidth) / 2 + workArea.X;
        var y = (workArea.Height - WindowHeight) / 2 + workArea.Y;
        _appWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        args.Cancel = true;
        Hide();

        // With the window as the recording UI, closing it cancels the recording (as before).
        // In tray mode the window is optional and the tray icon keeps showing the recording,
        // so hiding the window must not discard it.
        if (App.ConfigService.Config.ShowWindowWhileRecording &&
            App.MainViewModel.CurrentState == Models.AppState.Recording)
        {
            App.MainViewModel.CancelRecordingCommand.Execute(null);
        }
    }

    public void ShowAndActivate()
    {
        // Center first so the window appears at the right position
        CenterOnScreen();
        _appWindow.Show();

        // Re-assert always-on-top after showing from hidden state
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
        }

        this.Activate();
        RootPage.SetWindowVisible(true);
    }

    public void ForceClose()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        _iconReady?.Dispose();
        _iconRecording?.Dispose();
        _iconTranscribing?.Dispose();
        _iconDone?.Dispose();
        _iconError?.Dispose();
        _outcomeBadgeTimer?.Stop();
        _appWindow.Closing -= OnClosing;
        this.Close();
    }

    public void ShowHistoryWindow()
    {
        var historyWindow = new HistoryWindow();
        historyWindow.Activate();
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
