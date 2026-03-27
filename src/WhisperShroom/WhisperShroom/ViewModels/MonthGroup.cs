using WhisperShroom.Helpers;

namespace WhisperShroom.ViewModels;

public sealed class MonthGroup
{
    public int Year { get; }
    public int Month { get; }
    public string MonthLabel { get; }
    public string MonthSummary { get; }
    public List<DayGroup> Days { get; }
    public bool IsCurrentMonth { get; }

    public MonthGroup(int year, int month, List<DayGroup> days)
    {
        Year = year;
        Month = month;
        Days = days;
        IsCurrentMonth = year == DateTime.Today.Year && month == DateTime.Today.Month;
        MonthLabel = new DateTime(year, month, 1).ToString("MMMM yyyy");
        MonthSummary = FormatSummary(days);
    }

    private static string FormatSummary(List<DayGroup> days)
    {
        var totalEntries = days.Sum(d => d.Entries.Count);
        var totalCost = days.Sum(d => d.Entries.Sum(e => e.CostEur ?? 0m));

        var parts = new List<string>
        {
            $"{totalEntries} transcription{(totalEntries != 1 ? "s" : "")}",
            $"{days.Count} day{(days.Count != 1 ? "s" : "")}"
        };

        if (totalCost > 0)
            parts.Add(CostCalculator.FormatCostEur(totalCost));

        return string.Join(" | ", parts);
    }
}
