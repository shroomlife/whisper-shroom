using WhisperShroom.Helpers;
using WhisperShroom.Models;

namespace WhisperShroom.ViewModels;

public sealed class DayGroup
{
    public DateTime Date { get; }
    public string DateLabel { get; }
    public string DaySummary { get; }
    public List<TranscriptionEntry> Entries { get; }

    public DayGroup(DateTime date, List<TranscriptionEntry> entries)
    {
        Date = date;
        Entries = entries;
        DateLabel = FormatDate(date);
        DaySummary = FormatSummary(entries);
    }

    private static string FormatDate(DateTime date)
    {
        if (date.Date == DateTime.Today) return "Today";
        if (date.Date == DateTime.Today.AddDays(-1)) return "Yesterday";
        return date.ToString("dddd, MMMM d, yyyy");
    }

    private static string FormatSummary(List<TranscriptionEntry> entries)
    {
        var completed = entries.Where(e => !e.IsPending).ToList();
        var pendingCount = entries.Count(e => e.IsPending);
        var count = entries.Count;

        var totalTokens = completed
            .Where(e => e.UsageType == "tokens")
            .Sum(e => e.ComputedTotalTokens);
        var totalSeconds = completed
            .Where(e => e.UsageType == "duration")
            .Sum(e => e.DurationSeconds ?? 0);
        var totalCost = completed
            .Sum(e => e.CostEur ?? 0m);

        var parts = new List<string> { $"{count} transcription{(count != 1 ? "s" : "")}" };

        if (pendingCount > 0)
            parts.Add($"{pendingCount} pending");

        if (totalTokens > 0)
            parts.Add($"{totalTokens} tokens");

        if (totalSeconds > 0)
            parts.Add($"{totalSeconds}s audio");

        if (totalCost > 0)
            parts.Add(CostCalculator.FormatCostEur(totalCost));

        return string.Join(" | ", parts);
    }
}
