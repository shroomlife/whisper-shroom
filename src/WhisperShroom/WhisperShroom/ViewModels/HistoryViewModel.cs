using WhisperShroom.Helpers;
using WhisperShroom.Models;

namespace WhisperShroom.ViewModels;

/// <summary>
/// Data access for the History window. Every read runs on the thread pool so the window
/// opens immediately and the UI thread only builds what the user actually expanded.
/// Writes go through <see cref="Services.HistoryService"/>, whose Changed event is the single
/// trigger for the window to refresh; no method here reloads on its own.
/// </summary>
public sealed class HistoryViewModel
{
    /// <summary>Same cap as before: Inlines disable the TextBlock fast path, so keep it small.</summary>
    public const int MaxSearchResults = 100;

    public Task<List<MonthGroup>> LoadMonthsAsync() =>
        Task.Run(() => App.HistoryService.GetMonthSummaries()
            .Select(s => new MonthGroup(s))
            .ToList());

    public Task<List<DayGroup>> LoadDaysAsync(int year, int month) =>
        Task.Run(() => App.HistoryService.GetDaySummaries(year, month)
            .Select(s => new DayGroup(s))
            .ToList());

    public Task<List<TranscriptionEntry>> LoadEntriesAsync(DateTime day) =>
        Task.Run(() => App.HistoryService.GetEntriesForDay(day));

    public Task<(List<TranscriptionEntry> Results, int TotalCount)> SearchAsync(string query) =>
        Task.Run(() => App.HistoryService.SearchEntries(query, MaxSearchResults));

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

        return outcome.Kind == TranscriptionWorkflow.OutcomeKind.Success;
    }

    public void DeleteEntry(string id) => App.HistoryService.DeleteEntry(id);

    public void DeleteDay(DateTime date) => App.HistoryService.DeleteEntriesByDate(date);

    public void DeleteAll() => App.HistoryService.DeleteAllEntries();
}
