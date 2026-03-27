using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WhisperShroom.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    public ObservableCollection<MonthGroup> MonthGroups { get; } = new();

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    public HistoryViewModel()
    {
        LoadHistory();
    }

    public void LoadHistory()
    {
        MonthGroups.Clear();

        var entries = App.HistoryService.GetAllEntries();

        // Group by month, then by day within each month
        var months = entries
            .GroupBy(e => new { e.Timestamp.LocalDateTime.Year, e.Timestamp.LocalDateTime.Month })
            .OrderByDescending(g => g.Key.Year)
            .ThenByDescending(g => g.Key.Month);

        foreach (var monthGroup in months)
        {
            var dayGroups = monthGroup
                .GroupBy(e => e.Timestamp.LocalDateTime.Date)
                .OrderByDescending(g => g.Key)
                .Select(g => new DayGroup(g.Key, g.ToList()))
                .ToList();

            MonthGroups.Add(new MonthGroup(monthGroup.Key.Year, monthGroup.Key.Month, dayGroups));
        }

        IsEmpty = MonthGroups.Count == 0;
    }

    public void DeleteEntry(string id)
    {
        App.HistoryService.DeleteEntry(id);
        LoadHistory();
    }

    public void DeleteDay(DateTime date)
    {
        App.HistoryService.DeleteEntriesByDate(date);
        LoadHistory();
    }

    public void DeleteAll()
    {
        App.HistoryService.DeleteAllEntries();
        LoadHistory();
    }
}
