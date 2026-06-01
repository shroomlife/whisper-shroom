using System.Net.Http.Headers;
using System.Text.Json;
using NAudio.Wave;
using WhisperShroom.Helpers;
using WhisperShroom.Models;

namespace WhisperShroom.Services;

public sealed class TranscriptionService : IDisposable
{
    // OpenAI's audio endpoint rejects uploads over 25 MB. Stay safely under it (decimal
    // and binary interpretations both covered) and split larger recordings purely by
    // file size — never by duration.
    private const long MaxUploadBytes = 24_000_000;

    private readonly HttpClient _http = new()
    {
        // Large recordings (multi-MB upload + Whisper processing) easily exceed the 100 s
        // HttpClient default, which would surface as a spurious timeout failure.
        Timeout = TimeSpan.FromMinutes(5)
    };

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
        var effectiveModel = model ?? TranscriptionModelHelper.DefaultModelId;

        // Small enough to upload in a single request.
        if (wavData.Length <= MaxUploadBytes)
            return await TranscribeChunkAsync(wavData, apiKey, language, effectiveModel, ct);

        // Too large for one upload — split into <=MaxUploadBytes WAV chunks (by file size),
        // transcribe each sequentially, then stitch the text and sum the usage.
        var chunks = SplitWav(wavData, MaxUploadBytes);
        if (chunks.Count <= 1)
            return await TranscribeChunkAsync(wavData, apiKey, language, effectiveModel, ct);

        var texts = new List<string>(chunks.Count);
        TranscriptionResult? aggregate = null;

        foreach (var chunk in chunks)
        {
            ct.ThrowIfCancellationRequested();
            var part = await TranscribeChunkAsync(chunk, apiKey, language, effectiveModel, ct);

            var partText = part.Text.Trim();
            if (partText.Length > 0)
                texts.Add(partText);

            aggregate = MergeUsage(aggregate, part);
        }

        var combined = string.Join(" ", texts);
        return (aggregate ?? new TranscriptionResult { Text = combined }) with { Text = combined };
    }

    /// <summary>Uploads a single WAV payload (already known to be within the size limit).</summary>
    private async Task<TranscriptionResult> TranscribeChunkAsync(byte[] wavData, string apiKey, string? language, string effectiveModel, CancellationToken ct)
    {
        using var content = new MultipartFormDataContent();

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

    /// <summary>
    /// Splits a PCM WAV byte array into chunks whose individual WAV file size stays at or
    /// below <paramref name="maxBytes"/>. Purely size-based and sample-frame aligned so a
    /// sample is never cut in half; duration is irrelevant.
    /// </summary>
    private static List<byte[]> SplitWav(byte[] wavData, long maxBytes)
    {
        using var reader = new WaveFileReader(new MemoryStream(wavData));
        var format = reader.WaveFormat;

        const int headerBytes = 44; // canonical PCM WAV header written by WaveFileWriter
        var blockAlign = Math.Max(format.BlockAlign, 1);

        var maxDataPerChunk = maxBytes - headerBytes;
        maxDataPerChunk -= maxDataPerChunk % blockAlign; // align down to a whole sample frame
        if (maxDataPerChunk <= 0)
            return [];

        var chunks = new List<byte[]>();
        var buffer = new byte[maxDataPerChunk];

        int read;
        while ((read = ReadFull(reader, buffer)) > 0)
            chunks.Add(BuildWav(format, buffer, read));

        return chunks;
    }

    /// <summary>Wraps raw PCM bytes in a fresh WAV container.</summary>
    private static byte[] BuildWav(WaveFormat format, byte[] data, int count)
    {
        using var ms = new MemoryStream();
        using (var writer = new WaveFileWriter(ms, format))
            writer.Write(data, 0, count);
        // MemoryStream.ToArray() is documented to work after the stream has been closed,
        // and WaveFileWriter.Dispose finalizes the RIFF/data sizes before closing it.
        return ms.ToArray();
    }

    private static int ReadFull(WaveStream reader, byte[] buffer)
    {
        var total = 0;
        int read;
        while (total < buffer.Length &&
               (read = reader.Read(buffer, total, buffer.Length - total)) > 0)
            total += read;
        return total;
    }

    /// <summary>Sums usage/cost fields across transcribed chunks.</summary>
    private static TranscriptionResult MergeUsage(TranscriptionResult? acc, TranscriptionResult part)
    {
        if (acc is null)
            return part;

        return acc with
        {
            UsageType = acc.UsageType ?? part.UsageType,
            InputTokens = AddNullable(acc.InputTokens, part.InputTokens),
            OutputTokens = AddNullable(acc.OutputTokens, part.OutputTokens),
            TotalTokens = AddNullable(acc.TotalTokens, part.TotalTokens),
            AudioTokens = AddNullable(acc.AudioTokens, part.AudioTokens),
            DurationSeconds = AddNullable(acc.DurationSeconds, part.DurationSeconds),
        };
    }

    private static int? AddNullable(int? a, int? b) =>
        a is null && b is null ? null : (a ?? 0) + (b ?? 0);

    public void Dispose()
    {
        _http.Dispose();
    }
}
