using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WhisperShroom.Models;
using WhisperShroom.ViewModels;

namespace WhisperShroom.Views;

public sealed partial class MainPage : Page
{
    private bool _windowVisible;

    public MainViewModel ViewModel => App.MainViewModel;

    public MainPage()
    {
        this.InitializeComponent();
        // The page and the view model both live for the whole app lifetime, so this
        // subscription never needs to be removed.
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>Called by <see cref="MainWindow"/> whenever it is shown or hidden.</summary>
    public void SetWindowVisible(bool visible)
    {
        _windowVisible = visible;
        UpdatePulseAnimation();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentState))
            UpdatePulseAnimation();
    }

    // The recording dot only animates while someone can see it; in tray mode the window
    // usually stays hidden for the whole recording.
    private void UpdatePulseAnimation()
    {
        if (_windowVisible && ViewModel.CurrentState == AppState.Recording)
            PulseAnimation.Begin();
        else
            PulseAnimation.Stop();
    }

    public Visibility IsState(AppState current, AppState target) =>
        current == target ? Visibility.Visible : Visibility.Collapsed;

    public Visibility BoolToVisible(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;
}
