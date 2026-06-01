using WhisperShroom.Models;
using WhisperShroom.Services;

namespace WhisperShroom.Helpers;

/// <summary>
/// Shared transcription core for pending (persisted) audio entries. Used by both the
/// main window flow and the History retry flow so the DB/file lifecycle stays consistent:
/// on success the entry is completed and the WAV deleted, otherwise the audio is kept and
/// the pending entry's error is updated.
/// </summary>
public static class TranscriptionWorkflow
{
    public enum OutcomeKind { Success, Hallucination, Failure }

    public sealed record Outcome(OutcomeKind Kind, TranscriptionResult? Result, string? ErrorMessage);

    /// <summary>Shown both when a hallucination is detected and when audio is silent.</summary>
    public const string NoSpeechMessage = "No speech detected — recording appears silent.";

    /// <summary>
    /// Transcribes an already-persisted pending entry and resolves its DB state.
    /// Never throws — failures are reported via the returned <see cref="Outcome"/>.
    /// </summary>
    public static async Task<Outcome> RunPendingAsync(
        string entryId, string audioPath, string apiKey, string? language, string model)
    {
        if (!File.Exists(audioPath))
        {
            const string msg = "Audio file no longer available.";
            App.HistoryService.UpdatePendingError(entryId, msg);
            return new Outcome(OutcomeKind.Failure, null, msg);
        }

        try
        {
            var wavData = await File.ReadAllBytesAsync(audioPath);
            var result = await Task.Run(() =>
                App.TranscriptionService.TranscribeAsync(wavData, apiKey, language, model));

            var trimmed = result.Text.Trim();

            if (HallucinationFilter.IsHallucination(trimmed))
            {
                App.HistoryService.UpdatePendingError(entryId, NoSpeechMessage);
                return new Outcome(OutcomeKind.Hallucination, null, NoSpeechMessage);
            }

            var completed = result with { Text = trimmed };
            App.HistoryService.CompletePendingEntry(entryId, completed);

            // Delete audio only after a successful transcription.
            try { File.Delete(audioPath); } catch { /* best effort */ }

            return new Outcome(OutcomeKind.Success, completed, null);
        }
        catch (Exception ex)
        {
            App.HistoryService.UpdatePendingError(entryId, ex.Message);
            return new Outcome(OutcomeKind.Failure, null, ex.Message);
        }
    }
}
