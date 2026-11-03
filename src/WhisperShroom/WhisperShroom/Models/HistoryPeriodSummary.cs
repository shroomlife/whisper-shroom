namespace WhisperShroom.Models;

/// <summary>
/// Aggregated history figures for one period (a month or a day), computed in SQL so the
/// History window never has to load every entry to show its group headers.
/// </summary>
/// <param name="PeriodStart">First day of the month, or the day itself.</param>
/// <param name="EntryCount">All entries, completed and pending.</param>
/// <param name="PendingCount">Entries not transcribed yet.</param>
/// <param name="DayCount">Distinct local days with entries (only meaningful for months).</param>
/// <param name="TotalTokens">Input + output tokens of completed token-billed entries.</param>
/// <param name="DurationSeconds">Audio seconds of completed duration-billed entries.</param>
/// <param name="CostEur">Estimated cost of completed entries, summed per model.</param>
public sealed record HistoryPeriodSummary(
    DateTime PeriodStart,
    int EntryCount,
    int PendingCount,
    int DayCount,
    long TotalTokens,
    long DurationSeconds,
    decimal CostEur);
