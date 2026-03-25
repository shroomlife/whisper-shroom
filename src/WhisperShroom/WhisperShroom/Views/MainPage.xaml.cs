using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WhisperShroom.Models;
using WhisperShroom.ViewModels;

namespace WhisperShroom.Views;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel => App.MainViewModel;

    public MainPage()
    {
        this.InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentState))
        {
            if (ViewModel.CurrentState == AppState.Recording)
                PulseAnimation.Begin();
            else
                PulseAnimation.Stop();
        }
    }

    public Visibility IsState(AppState current, AppState target) =>
        current == target ? Visibility.Visible : Visibility.Collapsed;

    public Visibility BoolToVisible(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;
}
