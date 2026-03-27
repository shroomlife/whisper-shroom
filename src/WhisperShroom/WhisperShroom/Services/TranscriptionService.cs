using System.Net.Http.Headers;
using System.Text.Json;
using WhisperShroom.Helpers;
using WhisperShroom.Models;

namespace WhisperShroom.Services;

public sealed class TranscriptionService : IDisposable
{
    private readonly HttpClient _http = new();

    /// <summary>
    /// Validates the API key by calling GET /v1/models and checking for any known transcription model.
    /// Returns null on success, or an error message on failure.
    /// </summary>
    public async Task<string?> ValidateApiKeyAsync(string apiKey, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            return $"Connection failed: {ex.Message}";
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            return "Invalid API key.";

        if (!response.IsSuccessStatusCode)
            return $"API returned {(int)response.StatusCode} {response.ReasonPhrase}.";

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var model in data.EnumerateArray())
            {
                if (model.TryGetProperty("id", out var id) &&
                    TranscriptionModelHelper.KnownTranscriptionModels.Contains(id.GetString() ?? ""))
                    return null; // success
            }
        }

        return "API key is valid but does not have access to any supported transcription model.";
    }

    /// <summary>
    /// Fetches available transcription models for the given API key.
    /// Returns only models that are known and accessible, in canonical display order.
    /// </summary>
    public async Task<List<string>> GetAvailableTranscriptionModelsAsync(string apiKey, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var found = new HashSet<string>();

        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var model in data.EnumerateArray())
            {
                if (model.TryGetProperty("id", out var id))
                {
                    var modelId = id.GetString() ?? "";
                    if (TranscriptionModelHelper.KnownTranscriptionModels.Contains(modelId))
                        found.Add(modelId);
                }
            }
        }

        // Return in canonical display order
        return TranscriptionModelHelper.GetOrderedModelIds()
            .Where(found.Contains)
            .ToList();
    }

    public async Task<TranscriptionResult> TranscribeAsync(byte[] wavData, string apiKey, string? language = null, string? model = null, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();

        var effectiveModel = model ?? TranscriptionModelHelper.DefaultModelId;

        var fileContent = new ByteArrayContent(wavData);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "file", "recording.wav");
        content.Add(new StringContent(effectiveModel), "model");

        if (language is not null)
            content.Add(new StringContent(language), "language");

        // whisper-1 needs verbose_json to get duration; gpt-4o-transcribe models return usage in json
        if (effectiveModel == "whisper-1")
            content.Add(new StringContent("verbose_json"), "response_format");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/transcriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = content;

        var response = await _http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            throw response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized =>
                    new HttpRequestException("Invalid API key. Please check your key in Settings."),
                System.Net.HttpStatusCode.TooManyRequests =>
                    new HttpRequestException("OpenAI rate limit reached. Please wait a moment and try again."),
                _ when statusCode >= 500 =>
                    new HttpRequestException("OpenAI service is temporarily unavailable. Please try again later."),
                _ =>
                    new HttpRequestException($"Transcription failed (HTTP {statusCode}). Please try again.")
            };
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var text = doc.RootElement.GetProperty("text").GetString() ?? "";
        var result = new TranscriptionResult { Text = text };

        if (doc.RootElement.TryGetProperty("usage", out var usage))
        {
            var usageType = usage.TryGetProperty("type", out var typeProp)
                ? typeProp.GetString()
                : null;

            if (usageType == "tokens")
            {
                int? audioTokens = null;
                if (usage.TryGetProperty("input_token_details", out var details) &&
                    details.TryGetProperty("audio_tokens", out var audioProp))
                {
                    audioTokens = audioProp.GetInt32();
                }

                result = result with
                {
                    UsageType = "tokens",
                    InputTokens = usage.GetProperty("input_tokens").GetInt32(),
                    OutputTokens = usage.GetProperty("output_tokens").GetInt32(),
                    TotalTokens = usage.GetProperty("total_tokens").GetInt32(),
                    AudioTokens = audioTokens
                };
            }
            else if (usageType == "duration")
            {
                result = result with
                {
                    UsageType = "duration",
                    DurationSeconds = usage.GetProperty("seconds").GetInt32()
                };
            }
        }

        return result;
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
