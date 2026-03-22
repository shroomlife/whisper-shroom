using System.Net.Http.Headers;
using System.Text.Json;

namespace WhisperShroom.Services;

public sealed class TranscriptionService : IDisposable
{
    private readonly HttpClient _http = new();

    /// <summary>
    /// Validates the API key by calling GET /v1/models and checking for whisper-1.
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
                if (model.TryGetProperty("id", out var id) && id.GetString() == "whisper-1")
                    return null; // success
            }
        }

        return "API key is valid but does not have access to the Whisper model.";
    }

    public async Task<string> TranscribeAsync(byte[] wavData, string apiKey, string? language = null, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();

        var fileContent = new ByteArrayContent(wavData);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "file", "recording.wav");
        content.Add(new StringContent("whisper-1"), "model");

        if (language is not null)
            content.Add(new StringContent(language), "language");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/transcriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = content;

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.GetProperty("text").GetString() ?? "";
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
