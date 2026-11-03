using WhisperShroom.Helpers;
using WhisperShroom.Models;

namespace WhisperShroom.ViewModels;

/// <summary>Header data for one day in the History window. Its entries load on expand.</summary>
public sealed class DayGroup
{
    public DateTime Date { get; }
    public string DateLabel { get; }
    public string DaySummary { get; }

    public DayGroup(HistoryPeriodSummary summary)
    {
        Date = summary.PeriodStart.Date;
        DateLabel = FormatDate(Date);
        DaySummary = FormatSummary(summary);
    }

    private static string FormatDate(DateTime date)
    {
        if (date == DateTime.Today) return "Today";
        if (date == DateTime.Today.AddDays(-1)) return "Yesterday";
        return date.ToString("dddd, MMMM d, yyyy");
    }

    private static string FormatSummary(HistoryPeriodSummary summary)
    {
        var count = summary.EntryCount;
        var parts = new List<string> { $"{count} transcription{(count != 1 ? "s" : "")}" };

        if (summary.PendingCount > 0)
            parts.Add($"{summary.PendingCount} pending");

        if (summary.TotalTokens > 0)
            parts.Add($"{summary.TotalTokens} tokens");

        if (summary.DurationSeconds > 0)
            parts.Add($"{summary.DurationSeconds}s audio");

        if (summary.CostEur > 0)
            parts.Add(CostCalculator.FormatCostEur(summary.CostEur));

        return string.Join(" | ", parts);
    }
}
