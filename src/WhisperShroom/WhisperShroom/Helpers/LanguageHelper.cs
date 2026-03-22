namespace WhisperShroom.Helpers;

public static class LanguageHelper
{
    public static List<string> AvailableLanguages { get; } =
    [
        "Auto-detect",
        "German (de)",
        "English (en)",
        "French (fr)",
        "Spanish (es)",
        "Italian (it)",
        "Portuguese (pt)",
        "Dutch (nl)",
        "Polish (pl)",
        "Russian (ru)",
        "Japanese (ja)",
        "Chinese (zh)",
        "Korean (ko)",
        "Arabic (ar)",
        "Hindi (hi)",
        "Turkish (tr)",
        "Swedish (sv)",
        "Czech (cs)",
        "Ukrainian (uk)"
    ];

    /// <summary>
    /// Converts a display name like "German (de)" to the ISO code "de".
    /// Returns null for "Auto-detect".
    /// </summary>
    public static string? ToCode(string displayName) =>
        displayName == "Auto-detect" ? null : displayName[^3..^1];

    /// <summary>
    /// Converts an ISO code like "de" to the display name "German (de)".
    /// Returns "Auto-detect" for null.
    /// </summary>
    public static string ToDisplayName(string? code) =>
        code is null
            ? "Auto-detect"
            : AvailableLanguages.FirstOrDefault(l => l.EndsWith($"({code})")) ?? $"Other ({code})";
}
