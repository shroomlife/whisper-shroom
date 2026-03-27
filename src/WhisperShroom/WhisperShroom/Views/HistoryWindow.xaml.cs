using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;
using WhisperShroom.Helpers;
using WhisperShroom.Models;
using WhisperShroom.ViewModels;

namespace WhisperShroom.Views;

public sealed partial class HistoryWindow : Window
{
    private readonly AppWindow _appWindow;
    private readonly HistoryViewModel _viewModel = new();

    private const int WindowWidth = 650;
    private const int WindowHeight = 600;

    public HistoryWindow()
    {
        this.InitializeComponent();

        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        _appWindow.Resize(new Windows.Graphics.SizeInt32(WindowWidth, WindowHeight));
        _appWindow.Title = "WhisperShroom - History";

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

        CenterOnScreen();
        BuildHistoryUI();
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

    private void BuildHistoryUI()
    {
        HistoryPanel.Children.Clear();

        if (_viewModel.IsEmpty)
        {
            EmptyState.Visibility = Visibility.Visible;
            HistoryScroller.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;
        HistoryScroller.Visibility = Visibility.Visible;

        foreach (var dayGroup in _viewModel.DayGroups)
        {
            var expander = new Expander
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                IsExpanded = dayGroup.Date == DateTime.Today,
                Margin = new Thickness(0, 0, 0, 4)
            };

            // Header with date and summary
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            headerPanel.Children.Add(new TextBlock
            {
                Text = dayGroup.DateLabel,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            headerPanel.Children.Add(new TextBlock
            {
                Text = dayGroup.DaySummary,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12
            });
            expander.Header = headerPanel;

            // Content: list of entries
            var entriesPanel = new StackPanel { Spacing = 6 };
            foreach (var entry in dayGroup.Entries)
            {
                entriesPanel.Children.Add(CreateEntryCard(entry));
            }
            expander.Content = entriesPanel;

            HistoryPanel.Children.Add(expander);
        }
    }

    private UIElement CreateEntryCard(TranscriptionEntry entry)
    {
        var card = new Grid
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto }
            }
        };

        // Text content
        var textBlock = new TextBlock
        {
            Text = entry.Text,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 3,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(textBlock, 0);
        Grid.SetRow(textBlock, 0);
        card.Children.Add(textBlock);

        // Action buttons
        var buttonsPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };

        var copyButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE8C8", FontSize = 14 },
            Padding = new Thickness(8, 6, 8, 6),
            Tag = entry.Text
        };
        ToolTipService.SetToolTip(copyButton, "Copy");
        copyButton.Click += OnCopyEntry;
        buttonsPanel.Children.Add(copyButton);

        var deleteButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 },
            Padding = new Thickness(8, 6, 8, 6),
            Tag = entry.Id
        };
        ToolTipService.SetToolTip(deleteButton, "Delete");
        deleteButton.Click += OnDeleteEntry;
        buttonsPanel.Children.Add(deleteButton);

        Grid.SetColumn(buttonsPanel, 1);
        Grid.SetRow(buttonsPanel, 0);
        Grid.SetRowSpan(buttonsPanel, 2);
        card.Children.Add(buttonsPanel);

        // Meta row: time + cost + usage + model
        var metaPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(0, 6, 0, 0)
        };

        metaPanel.Children.Add(new TextBlock
        {
            Text = entry.TimeDisplay,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });

        if (!string.IsNullOrEmpty(entry.CostDisplay))
        {
            metaPanel.Children.Add(new TextBlock
            {
                Text = entry.CostDisplay,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
        }

        if (!string.IsNullOrEmpty(entry.UsageDisplay))
        {
            metaPanel.Children.Add(new TextBlock
            {
                Text = entry.UsageDisplay,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
        }

        if (!string.IsNullOrEmpty(entry.Model))
        {
            metaPanel.Children.Add(new TextBlock
            {
                Text = entry.Model,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
            });
        }

        Grid.SetColumn(metaPanel, 0);
        Grid.SetRow(metaPanel, 1);
        card.Children.Add(metaPanel);

        return card;
    }

    private void OnCopyEntry(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string text)
        {
            var hWnd = WindowNative.GetWindowHandle(this);
            ClipboardHelper.CopyToClipboard(text, hWnd);
        }
    }

    private async void OnDeleteEntry(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string id)
            return;

        var dialog = new ContentDialog
        {
            Title = "Delete Transcription",
            Content = "Are you sure you want to delete this transcription? This cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            _viewModel.DeleteEntry(id);
            BuildHistoryUI();
        }
    }
}
