using System.Net.Http.Headers;
using System.Text.Json;

namespace WhisperShroom.Services;

public sealed class TranscriptionService : IDisposable
{
    private readonly HttpClient _http = new();

    public async Task<string> TranscribeAsync(byte[] wavData, string apiKey, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();

        var fileContent = new ByteArrayContent(wavData);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "file", "recording.wav");
        content.Add(new StringContent("whisper-1"), "model");
        content.Add(new StringContent("de"), "language");

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
