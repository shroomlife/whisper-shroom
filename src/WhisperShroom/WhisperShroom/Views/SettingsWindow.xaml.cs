using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using WinRT.Interop;
using WhisperShroom.Helpers;
using WhisperShroom.ViewModels;

namespace WhisperShroom.Views;

public sealed partial class SettingsWindow : Window
{
    private readonly AppWindow _appWindow;
    private bool _isRecordingHotkey;
    private string _hotkeyBeforeRecording = "";

    private const int WindowWidth = 500;

    public SettingsViewModel ViewModel { get; } = new();

    /// <summary>
    /// Raised when settings were saved successfully.
    /// </summary>
    public event Action? Saved;

    public SettingsWindow()
    {
        this.InitializeComponent();

        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        // Start with a tall window; will auto-shrink to content after layout
        _appWindow.Resize(new Windows.Graphics.SizeInt32(WindowWidth, 900));
        _appWindow.Title = "WhisperShroom - Settings";

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }

        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
            if (File.Exists(iconPath))
                _appWindow.SetIcon(iconPath);
        }
        catch { }

        // Move off-screen initially, then auto-size + center after layout
        _appWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));
        RootGrid.Loaded += OnContentLoaded;
    }

    private void OnContentLoaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Loaded -= OnContentLoaded;

        var scale = Content.XamlRoot.RasterizationScale;

        // Chrome (title bar) height in physical pixels
        var chromeHeight = _appWindow.Size.Height - (int)(RootGrid.ActualHeight * scale);

        // Measure content at current rendered width to get ideal height (DIPs → physical pixels)
        RootGrid.Measure(new Windows.Foundation.Size(RootGrid.ActualWidth, double.PositiveInfinity));
        var desiredHeight = (int)Math.Ceiling(RootGrid.DesiredSize.Height * scale) + chromeHeight;

        // Cap at 90% of work area
        var displayArea = DisplayArea.GetFromWindowId(
            _appWindow.Id, DisplayAreaFallback.Primary);
        var maxHeight = (int)(displayArea.WorkArea.Height * 0.9);
        var finalHeight = Math.Min(desiredHeight, maxHeight);

        _appWindow.Resize(new Windows.Graphics.SizeInt32(WindowWidth, finalHeight));
        CenterOnScreen();
    }

    private void CenterOnScreen()
    {
        var displayArea = DisplayArea.GetFromWindowId(
            _appWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;

        var x = (workArea.Width - _appWindow.Size.Width) / 2 + workArea.X;
        var y = (workArea.Height - _appWindow.Size.Height) / 2 + workArea.Y;
        _appWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        _isRecordingHotkey = true;
        _hotkeyBeforeRecording = ViewModel.Hotkey;
        HotkeyBox.Text = "Press a key combination...";

        // Unregister global hotkey so it doesn't trigger during recording
        App.HotkeyService.Unregister();
    }

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isRecordingHotkey)
        {
            _isRecordingHotkey = false;
            HotkeyBox.Text = _hotkeyBeforeRecording;
            ViewModel.Hotkey = _hotkeyBeforeRecording;
        }

        // Re-register global hotkey
        var hotkey = App.ConfigService.Config.Hotkey;
        App.HotkeyService.Register(hotkey, App.MainViewModel.ToggleRecording);
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_isRecordingHotkey)
            return;

        e.Handled = true;

        // Escape cancels recording
        if (e.Key == VirtualKey.Escape)
        {
            _isRecordingHotkey = false;
            ViewModel.Hotkey = _hotkeyBeforeRecording;
            HotkeyBox.Text = _hotkeyBeforeRecording;
            ApiKeyBox.Focus(FocusState.Programmatic);
            return;
        }

        // Skip pure modifier presses — wait for a non-modifier key
        if (HotkeyParser.IsModifier(e.Key))
            return;

        // Read modifier state
        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var alt = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var win = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.LeftWindows)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)
            || InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.RightWindows)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        var formatted = HotkeyParser.Format(e.Key, ctrl, shift, alt, win);
        if (formatted is null)
            return;

        _isRecordingHotkey = false;
        ViewModel.Hotkey = formatted;
        HotkeyBox.Text = formatted;
        // Move focus away from hotkey box
        ApiKeyBox.Focus(FocusState.Programmatic);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.ApiKey))
        {
            ErrorInfo.Message = "API key is required!";
            ErrorInfo.IsOpen = true;
            return;
        }

        try
        {
            Helpers.HotkeyParser.Parse(
                string.IsNullOrWhiteSpace(ViewModel.Hotkey) ? "ctrl+shift+e" : ViewModel.Hotkey);
        }
        catch (ArgumentException ex)
        {
            ErrorInfo.Message = $"Invalid hotkey: {ex.Message}";
            ErrorInfo.IsOpen = true;
            return;
        }

        if (!ViewModel.Save())
        {
            ErrorInfo.Message = "Failed to save settings.";
            ErrorInfo.IsOpen = true;
            return;
        }

        Saved?.Invoke();
        this.Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        this.Close();
    }
}
