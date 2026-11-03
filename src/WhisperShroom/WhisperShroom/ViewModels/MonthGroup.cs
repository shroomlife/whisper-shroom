using WhisperShroom.Helpers;
using WhisperShroom.Models;

namespace WhisperShroom.ViewModels;

/// <summary>Header data for one month in the History window. Its days load on expand.</summary>
public sealed class MonthGroup
{
    public int Year { get; }
    public int Month { get; }
    public string Key { get; }
    public string MonthLabel { get; }
    public string MonthSummary { get; }

    public MonthGroup(HistoryPeriodSummary summary)
    {
        Year = summary.PeriodStart.Year;
        Month = summary.PeriodStart.Month;
        Key = $"{Year:D4}-{Month:D2}";
        MonthLabel = summary.PeriodStart.ToString("MMMM yyyy");
        MonthSummary = FormatSummary(summary);
    }

    private static string FormatSummary(HistoryPeriodSummary summary)
    {
        var parts = new List<string>
        {
            $"{summary.EntryCount} transcription{(summary.EntryCount != 1 ? "s" : "")}",
            $"{summary.DayCount} day{(summary.DayCount != 1 ? "s" : "")}"
        };

        if (summary.CostEur > 0)
            parts.Add(CostCalculator.FormatCostEur(summary.CostEur));

        return string.Join(" | ", parts);
    }
}
