using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using WinRT.Interop;
using WhisperShroom.Helpers;
using WhisperShroom.Models;
using WhisperShroom.ViewModels;

namespace WhisperShroom.Views;

public sealed partial class SetupWizardWindow : Window
{
    private readonly AppWindow _appWindow;
    private bool _isRecordingHotkey;
    private string _hotkeyBeforeRecording = "";

    private const int WindowWidth = 550;
    private const int WindowHeight = 680;

    public SetupWizardViewModel ViewModel { get; } = new();

    /// <summary>
    /// Raised when the wizard completed and config was saved.
    /// </summary>
    public event Action? Completed;

    public SetupWizardWindow()
    {
        this.InitializeComponent();

        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        _appWindow.Resize(new Windows.Graphics.SizeInt32(WindowWidth, WindowHeight));
        _appWindow.Title = "WhisperShroom - Setup";

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

        CenterOnScreen();

        ViewModel.Completed += OnWizardCompleted;
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

    // --- Hotkey recording (adapted from SettingsWindow, no hotkey service calls) ---

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        _isRecordingHotkey = true;
        _hotkeyBeforeRecording = ViewModel.Hotkey;
        HotkeyBox.Text = "Press a key combination...";
    }

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isRecordingHotkey)
        {
            _isRecordingHotkey = false;
            HotkeyBox.Text = _hotkeyBeforeRecording;
            ViewModel.Hotkey = _hotkeyBeforeRecording;
        }
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
            NextButton.Focus(FocusState.Programmatic);
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
        NextButton.Focus(FocusState.Programmatic);
    }

    // --- Navigation ---

    private void OnNext(object sender, RoutedEventArgs e)
    {
        ViewModel.GoNextCommand.Execute(null);
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        ViewModel.GoBackCommand.Execute(null);
    }

    private void OnWizardCompleted()
    {
        Completed?.Invoke();
        this.Close();
    }

    // --- XAML binding helpers ---

    public Visibility IsStep(WizardStep current, WizardStep target) =>
        current == target ? Visibility.Visible : Visibility.Collapsed;

    public Visibility BoolToVisible(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    public string NextButtonText(bool isLastStep) =>
        isLastStep ? "Finish" : "Next";
}
