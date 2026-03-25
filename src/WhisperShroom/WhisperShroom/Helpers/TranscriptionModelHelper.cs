namespace WhisperShroom.Helpers;

public static class TranscriptionModelHelper
{
    // Canonical order for display. Add new models here to extend support.
    private static readonly IReadOnlyList<string> OrderedModelIds =
    [
        "whisper-1",
        "gpt-4o-transcribe",
        "gpt-4o-mini-transcribe",
    ];

    private static readonly Dictionary<string, string> DisplayNames = new()
    {
        ["whisper-1"] = "Whisper 1",
        ["gpt-4o-transcribe"] = "GPT-4o Transcribe",
        ["gpt-4o-mini-transcribe"] = "GPT-4o Mini Transcribe",
    };

    /// <summary>
    /// HashSet for O(1) membership checks (e.g. when filtering API responses).
    /// </summary>
    public static readonly HashSet<string> KnownTranscriptionModels =
        new(OrderedModelIds);

    public const string DefaultModelId = "whisper-1";

    /// <summary>
    /// Returns a user-friendly display name for a model ID.
    /// Falls back to the raw ID for unknown models.
    /// </summary>
    public static string ToDisplayName(string modelId) =>
        DisplayNames.TryGetValue(modelId, out var name) ? name : modelId;

    /// <summary>
    /// Returns the model ID for a display name.
    /// Falls back to the raw display name if not found.
    /// </summary>
    public static string ToModelId(string displayName) =>
        DisplayNames.FirstOrDefault(kv => kv.Value == displayName).Key ?? displayName;

    /// <summary>
    /// All known model IDs in canonical display order (for fallback when API is unavailable).
    /// </summary>
    public static IReadOnlyList<string> GetOrderedModelIds() => OrderedModelIds;

    /// <summary>
    /// All known model display names in canonical order (for fallback when API is unavailable).
    /// </summary>
    public static List<string> AllDisplayNames() =>
        OrderedModelIds.Select(id => DisplayNames[id]).ToList();
}
