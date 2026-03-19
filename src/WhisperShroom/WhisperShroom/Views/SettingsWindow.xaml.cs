using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using WhisperShroom.ViewModels;

namespace WhisperShroom.Views;

public sealed partial class SettingsWindow : Window
{
    private readonly AppWindow _appWindow;

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

        _appWindow.Resize(new Windows.Graphics.SizeInt32(500, 550));
        _appWindow.Title = "WhisperShroom - Einstellungen";

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
    }

    private void CenterOnScreen()
    {
        var displayArea = DisplayArea.GetFromWindowId(
            _appWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;

        var x = (workArea.Width - 500) / 2 + workArea.X;
        var y = (workArea.Height - 550) / 2 + workArea.Y;
        _appWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.ApiKey))
        {
            ErrorInfo.Message = "API-Key wird benötigt!";
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
            ErrorInfo.Message = $"Ungültige Tastenkombination: {ex.Message}";
            ErrorInfo.IsOpen = true;
            return;
        }

        if (!ViewModel.Save())
        {
            ErrorInfo.Message = "Einstellungen konnten nicht gespeichert werden.";
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
