namespace WhisperShroom.Models;

public sealed class TranscriptionEntry
{
    public string Id { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; }
    public string Text { get; set; } = "";
    public string Model { get; set; } = "";
    public string? Language { get; set; }
    public string? UsageType { get; set; }
    public int? InputTokens { get; set; }
    public int? AudioTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? DurationSeconds { get; set; }

    public string TimeDisplay => Timestamp.LocalDateTime.ToString("HH:mm");

    public string UsageDisplay => UsageType switch
    {
        "tokens" => $"{ComputedTotalTokens} tokens",
        "duration" when DurationSeconds is not null => $"{DurationSeconds}s audio",
        _ => ""
    };

    public int ComputedTotalTokens => (InputTokens ?? 0) + (OutputTokens ?? 0);
}
