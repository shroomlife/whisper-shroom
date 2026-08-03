namespace WhisperShroom.Helpers;

public static class TranscriptionModelHelper
{
    // Canonical order for display. Add new models here to extend support.
    private static readonly IReadOnlyList<string> OrderedModelIds =
    [
        "gpt-transcribe",
        "whisper-1",
        "gpt-4o-transcribe",
        "gpt-4o-mini-transcribe",
    ];

    private static readonly Dictionary<string, string> DisplayNames = new()
    {
        ["gpt-transcribe"] = "GPT Transcribe",
        ["whisper-1"] = "Whisper 1",
        ["gpt-4o-transcribe"] = "GPT-4o Transcribe",
        ["gpt-4o-mini-transcribe"] = "GPT-4o Mini Transcribe",
    };

    /// <summary>
    /// Models that take the plural <c>languages[]</c> form field instead of the singular
    /// <c>language</c>. Sending both is rejected by the API, so the two are mutually exclusive.
    /// </summary>
    private static readonly HashSet<string> LanguagesArrayModels =
    [
        "gpt-transcribe",
    ];

    /// <summary>
    /// HashSet for O(1) membership checks (e.g. when filtering API responses).
    /// </summary>
    public static readonly HashSet<string> KnownTranscriptionModels =
        new(OrderedModelIds);

    public const string DefaultModelId = "gpt-transcribe";

    /// <summary>
    /// True if the model expects the language hint as <c>languages[]</c> rather than <c>language</c>.
    /// </summary>
    public static bool UsesLanguagesArray(string modelId) =>
        LanguagesArrayModels.Contains(modelId);

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
