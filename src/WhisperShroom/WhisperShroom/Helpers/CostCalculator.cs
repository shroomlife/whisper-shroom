namespace WhisperShroom.Helpers;

internal static class CostCalculator
{
    // USD to EUR conversion rate (approximate, updated periodically)
    private const decimal UsdToEur = 0.92m;

    // Pricing per 1M tokens (USD) - from https://developers.openai.com/api/docs/pricing/
    private static readonly Dictionary<string, (decimal InputPer1M, decimal OutputPer1M)> TokenPricing = new()
    {
        ["gpt-4o-transcribe"] = (6.00m, 6.00m),
        ["gpt-4o-mini-transcribe"] = (3.00m, 3.00m),
        ["gpt-4o-mini-transcribe-2025-12-15"] = (3.00m, 3.00m),
    };

    // Pricing per minute (USD) for duration-based models
    private static readonly Dictionary<string, decimal> MinutePricing = new()
    {
        ["whisper-1"] = 0.006m,
    };

    /// <summary>
    /// Calculates the estimated cost in EUR for a transcription.
    /// Returns null if pricing data is unavailable for the model.
    /// </summary>
    public static decimal? CalculateCostEur(
        string? model,
        string? usageType,
        int? inputTokens,
        int? outputTokens,
        int? durationSeconds)
    {
        if (model is null) return null;

        decimal costUsd;

        if (usageType == "tokens" && inputTokens is not null)
        {
            if (!TokenPricing.TryGetValue(model, out var pricing))
                return null;

            costUsd = ((inputTokens.Value * pricing.InputPer1M) +
                       ((outputTokens ?? 0) * pricing.OutputPer1M)) / 1_000_000m;
        }
        else if (usageType == "duration" && durationSeconds is not null)
        {
            if (!MinutePricing.TryGetValue(model, out var pricePerMinute))
                return null;

            costUsd = (durationSeconds.Value / 60m) * pricePerMinute;
        }
        else
        {
            return null;
        }

        return Math.Round(costUsd * UsdToEur, 6);
    }

    /// <summary>
    /// Formats a cost value for display (e.g., "0.0003 EUR" or "&lt; 0.01 ct").
    /// </summary>
    public static string FormatCostEur(decimal costEur)
    {
        // Show in cents if less than 1 EUR
        var cents = costEur * 100;

        if (cents < 0.01m)
            return "< 0.01 ct";

        if (cents < 1m)
            return $"{cents:F2} ct";

        if (costEur < 1m)
            return $"{costEur:F4} EUR";

        return $"{costEur:F2} EUR";
    }
}
