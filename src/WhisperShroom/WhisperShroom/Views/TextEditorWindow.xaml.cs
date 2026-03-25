using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace WhisperShroom.Views;

public sealed partial class TextEditorWindow : Window
{
    private readonly AppWindow _appWindow;

    /// <summary>
    /// Raised when the user clicks Save. Passes the edited text.
    /// </summary>
    public event Action<string>? Saved;

    public TextEditorWindow(string title, string initialText)
    {
        this.InitializeComponent();

        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        _appWindow.Resize(new Windows.Graphics.SizeInt32(450, 350));
        _appWindow.Title = $"WhisperShroom - {title}";

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = false;
        }

        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
            if (File.Exists(iconPath))
                _appWindow.SetIcon(iconPath);
        }
        catch { }

        EditorBox.Text = initialText;

        // Center on screen after layout
        RootGrid.Loaded += (_, _) => CenterOnScreen();
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

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Saved?.Invoke(EditorBox.Text);
        this.Close();
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        EditorBox.Text = "";
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        this.Close();
    }
}
