using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
    private readonly DispatcherQueue _dispatcher;

    // Tracks the intended scroll target independently of VerticalOffset (which lags during animation)
    private double _scrollTarget = double.NaN;

    private const int WindowWidth = 650;
    private const int WindowHeight = 600;

    public HistoryWindow()
    {
        this.InitializeComponent();

        _dispatcher = DispatcherQueue;

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

        // React to new transcriptions or deletions from other windows
        App.HistoryService.Changed += OnHistoryChanged;
        _appWindow.Closing += OnClosing;
    }

    private void OnHistoryChanged()
    {
        _dispatcher.TryEnqueue(() =>
        {
            _viewModel.LoadHistory();
            BuildHistoryUI();
        });
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        App.HistoryService.Changed -= OnHistoryChanged;
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
        _scrollTarget = double.NaN; // reset so next scroll reads fresh VerticalOffset
        HistoryPanel.Children.Clear();

        if (_viewModel.IsEmpty)
        {
            EmptyState.Visibility = Visibility.Visible;
            HistoryScroller.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;
        HistoryScroller.Visibility = Visibility.Visible;

        foreach (var monthGroup in _viewModel.MonthGroups)
        {
            var monthExpander = new Expander
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                IsExpanded = monthGroup.IsCurrentMonth,
                Margin = new Thickness(0, 0, 0, 8)
            };
            monthExpander.AddHandler(UIElement.PointerWheelChangedEvent,
                new PointerEventHandler(OnForwardScroll), true);

            // Month header
            var monthHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            monthHeader.Children.Add(new TextBlock
            {
                Text = monthGroup.MonthLabel,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center
            });
            monthHeader.Children.Add(new TextBlock
            {
                Text = monthGroup.MonthSummary,
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12
            });
            monthExpander.Header = monthHeader;

            // Days inside the month
            var daysPanel = new StackPanel { Spacing = 4 };

            foreach (var dayGroup in monthGroup.Days)
            {
                var dayExpander = new Expander
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    IsExpanded = dayGroup.Date == DateTime.Today,
                    Margin = new Thickness(0, 0, 0, 2),
                    Padding = new Thickness(0)
                };
                dayExpander.AddHandler(UIElement.PointerWheelChangedEvent,
                    new PointerEventHandler(OnForwardScroll), true);

                // Day header
                var dayHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                dayHeader.Children.Add(new TextBlock
                {
                    Text = dayGroup.DateLabel,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                });
                dayHeader.Children.Add(new TextBlock
                {
                    Text = dayGroup.DaySummary,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12
                });
                dayExpander.Header = dayHeader;

                // Entries inside the day
                var entriesPanel = new StackPanel { Spacing = 6, Margin = new Thickness(8, 6, 8, 0) };
                foreach (var entry in dayGroup.Entries)
                {
                    entriesPanel.Children.Add(CreateEntryCard(entry));
                }

                // "Delete Day" button at the bottom of the day
                var deleteDayButton = new Button
                {
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            new FontIcon { Glyph = "\uE74D", FontSize = 13 },
                            new TextBlock { Text = "Delete this day" }
                        }
                    },
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 4),
                    Padding = new Thickness(12, 6, 12, 6),
                    Tag = dayGroup.Date
                };
                ToolTipService.SetToolTip(deleteDayButton, $"Delete all transcriptions from {dayGroup.DateLabel}");
                deleteDayButton.Click += OnDeleteDay;
                deleteDayButton.AddHandler(UIElement.PointerWheelChangedEvent,
                    new PointerEventHandler(OnForwardScroll), true);
                entriesPanel.Children.Add(deleteDayButton);

                dayExpander.Content = entriesPanel;

                daysPanel.Children.Add(dayExpander);
            }

            monthExpander.Content = daysPanel;
            HistoryPanel.Children.Add(monthExpander);
        }

        // "Clear All History" button at the very bottom
        var clearAllButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new FontIcon { Glyph = "\uE74D", FontSize = 14 },
                    new TextBlock { Text = "Clear all history" }
                }
            },
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 16, 0, 8),
            Padding = new Thickness(16, 8, 16, 8)
        };
        ToolTipService.SetToolTip(clearAllButton, "Delete all transcriptions");
        clearAllButton.Click += OnClearAllHistory;
        clearAllButton.AddHandler(UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(OnForwardScroll), true);
        HistoryPanel.Children.Add(clearAllButton);
    }

    private UIElement CreateEntryCard(TranscriptionEntry entry)
    {
        // Use Border as outer container — it passes pointer wheel events
        // through to the parent ScrollViewer reliably, unlike Grid with Background
        var border = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1)
        };

        var card = new Grid
        {
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

        // Text content — forward pointer wheel to ScrollViewer
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
        copyButton.AddHandler(UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(OnForwardScroll), true);
        buttonsPanel.Children.Add(copyButton);

        var deleteButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 },
            Padding = new Thickness(8, 6, 8, 6),
            Tag = entry.Id
        };
        ToolTipService.SetToolTip(deleteButton, "Delete");
        deleteButton.Click += OnDeleteEntry;
        deleteButton.AddHandler(UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(OnForwardScroll), true);
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

        border.Child = card;
        return border;
    }

    /// <summary>
    /// Forwards pointer wheel events from elements that consume them (Buttons, Expander headers)
    /// directly to the outer ScrollViewer via ChangeView.
    ///
    /// Why not just set e.Handled = false and let it bubble?
    ///   WinUI 3 Buttons mark PointerWheelChanged as Handled at the control level — resetting
    ///   Handled to false in a handledEventsToo handler does not re-enable bubbling in WinRT.
    ///
    /// Why track _scrollTarget instead of reading VerticalOffset?
    ///   ChangeView with animation is async; VerticalOffset doesn't update until the animation
    ///   completes. Rapid wheel ticks would all compute the same target (from the stale offset),
    ///   making the list barely move. Accumulating into _scrollTarget fixes this.
    /// </summary>
    private void OnForwardScroll(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(null).Properties.MouseWheelDelta;
        // Fall back to current offset the first time (after a list rebuild)
        if (double.IsNaN(_scrollTarget))
            _scrollTarget = HistoryScroller.VerticalOffset;

        _scrollTarget = Math.Max(0, Math.Min(_scrollTarget - delta, HistoryScroller.ScrollableHeight));
        HistoryScroller.ChangeView(null, _scrollTarget, null, false); // false = keep smooth animation
        e.Handled = true;
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

    private async void OnDeleteDay(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not DateTime date)
            return;

        var dateLabel = date == DateTime.Today ? "today"
            : date == DateTime.Today.AddDays(-1) ? "yesterday"
            : date.ToString("MMMM d, yyyy");

        var dialog = new ContentDialog
        {
            Title = "Delete Day",
            Content = $"Are you sure you want to delete all transcriptions from {dateLabel}? This cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            _viewModel.DeleteDay(date);
            BuildHistoryUI();
        }
    }

    private async void OnClearAllHistory(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Clear All History",
            Content = "Are you sure you want to delete ALL transcriptions? This cannot be undone.",
            PrimaryButtonText = "Delete All",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            _viewModel.DeleteAll();
            BuildHistoryUI();
        }
    }
}
