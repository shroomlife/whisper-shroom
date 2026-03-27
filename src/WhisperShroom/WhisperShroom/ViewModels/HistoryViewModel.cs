using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WhisperShroom.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    public ObservableCollection<DayGroup> DayGroups { get; } = new();

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    public HistoryViewModel()
    {
        LoadHistory();
    }

    public void LoadHistory()
    {
        DayGroups.Clear();

        var entries = App.HistoryService.GetAllEntries();
        var groups = entries
            .GroupBy(e => e.Timestamp.LocalDateTime.Date)
            .OrderByDescending(g => g.Key);

        foreach (var group in groups)
        {
            DayGroups.Add(new DayGroup(group.Key, group.ToList()));
        }

        IsEmpty = DayGroups.Count == 0;
    }

    public void DeleteEntry(string id)
    {
        App.HistoryService.DeleteEntry(id);
        LoadHistory();
    }
}
