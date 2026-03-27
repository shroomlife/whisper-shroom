namespace WhisperShroom.Models;

public sealed record TranscriptionResult
{
    public required string Text { get; init; }
    public string? UsageType { get; init; }
    public int? InputTokens { get; init; }
    public int? AudioTokens { get; init; }
    public int? OutputTokens { get; init; }
    public int? TotalTokens { get; init; }
    public int? DurationSeconds { get; init; }
}
