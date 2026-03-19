namespace WhisperShroom.Services;

public static class HallucinationFilter
{
    private static readonly HashSet<string> KnownHallucinations = new(StringComparer.OrdinalIgnoreCase)
    {
        "untertitel der amara.org-community",
        "untertitel von der amara.org-community",
        "untertitel im auftrag des zdf für funk, 2017",
        "untertitel im auftrag des zdf, 2017",
        "untertitel im auftrag des zdf, 2020",
        "swr 2020",
        "swr 2021",
        "copyright wdr 2020",
        "copyright wdr 2021",
        "vielen dank fürs zuschauen!",
        "vielen dank für's zuschauen!",
        "bis zum nächsten mal!",
        "danke fürs zuschauen!",
        "danke für's zuschauen!",
        "tschüss!",
    };

    public static bool IsHallucination(string text)
    {
        var normalized = text.Trim().TrimEnd('.');
        return KnownHallucinations.Contains(normalized);
    }
}
