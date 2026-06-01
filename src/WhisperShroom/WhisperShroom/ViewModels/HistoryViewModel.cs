using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WhisperShroom.Helpers;
using WhisperShroom.Models;
using WhisperShroom.Services;

namespace WhisperShroom.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    public ObservableCollection<MonthGroup> MonthGroups { get; } = new();

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = "";

    public HistoryViewModel()
    {
        LoadHistory();
    }

    public bool HasSearchQuery => !string.IsNullOrWhiteSpace(SearchQuery);

    /// <summary>
    /// Returns entries matching the current search query (case-insensitive).
    /// </summary>
    public (List<TranscriptionEntry> Results, int TotalCount) SearchEntries()
    {
        if (!HasSearchQuery) return ([], 0);

        var query = SearchQuery.Trim();
        var all = App.HistoryService.GetAllEntries()
            .Where(e => !e.IsPending && e.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Cap displayed results to keep UI responsive (Inlines disable TextBlock fast-path)
        const int maxResults = 100;
        return (all.Take(maxResults).ToList(), all.Count);
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

    public async Task<bool> RetryEntryAsync(TranscriptionEntry entry)
    {
        if (entry.AudioPath is null || !File.Exists(entry.AudioPath))
            return false;

        var config = App.ConfigService.Config;
        var apiKey = config.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            return false;

        var model = entry.Model;
        if (string.IsNullOrEmpty(model))
            model = config.Model ?? TranscriptionModelHelper.DefaultModelId;

        var language = entry.Language ?? config.Language;

        var outcome = await TranscriptionWorkflow.RunPendingAsync(
            entry.Id, entry.AudioPath, apiKey, language, model);

        LoadHistory();
        return outcome.Kind == TranscriptionWorkflow.OutcomeKind.Success;
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
