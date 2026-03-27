using Microsoft.Data.Sqlite;
using WhisperShroom.Models;

namespace WhisperShroom.Services;

public sealed class HistoryService
{
    private readonly string _connectionString;

    /// <summary>
    /// Raised when the history data changes (entry added or deleted).
    /// </summary>
    public event Action? Changed;

    public HistoryService()
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WhisperShroom",
            "history.db");

        _connectionString = $"Data Source={dbPath}";
    }

    public void InitializeDatabase()
    {
        var dir = Path.GetDirectoryName(_connectionString.Replace("Data Source=", ""))!;
        Directory.CreateDirectory(dir);

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS transcriptions (
                id TEXT PRIMARY KEY,
                timestamp TEXT NOT NULL,
                text TEXT NOT NULL,
                model TEXT,
                language TEXT,
                usage_type TEXT,
                input_tokens INTEGER,
                audio_tokens INTEGER,
                output_tokens INTEGER,
                duration_seconds INTEGER
            )
            """;
        command.ExecuteNonQuery();
    }

    public void AddEntry(TranscriptionResult result, string? model, string? language)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO transcriptions (id, timestamp, text, model, language, usage_type, input_tokens, audio_tokens, output_tokens, duration_seconds)
            VALUES (@id, @timestamp, @text, @model, @language, @usageType, @inputTokens, @audioTokens, @outputTokens, @durationSeconds)
            """;

        command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("@timestamp", DateTimeOffset.Now.ToString("o"));
        command.Parameters.AddWithValue("@text", result.Text);
        command.Parameters.AddWithValue("@model", (object?)model ?? DBNull.Value);
        command.Parameters.AddWithValue("@language", (object?)language ?? DBNull.Value);
        command.Parameters.AddWithValue("@usageType", (object?)result.UsageType ?? DBNull.Value);
        command.Parameters.AddWithValue("@inputTokens", (object?)result.InputTokens ?? DBNull.Value);
        command.Parameters.AddWithValue("@audioTokens", (object?)result.AudioTokens ?? DBNull.Value);
        command.Parameters.AddWithValue("@outputTokens", (object?)result.OutputTokens ?? DBNull.Value);
        command.Parameters.AddWithValue("@durationSeconds", (object?)result.DurationSeconds ?? DBNull.Value);

        command.ExecuteNonQuery();
        Changed?.Invoke();
    }

    public List<TranscriptionEntry> GetAllEntries()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, timestamp, text, model, language, usage_type, input_tokens, audio_tokens, output_tokens, duration_seconds
            FROM transcriptions
            ORDER BY timestamp DESC
            """;

        var entries = new List<TranscriptionEntry>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            entries.Add(new TranscriptionEntry
            {
                Id = reader.GetString(0),
                Timestamp = DateTimeOffset.Parse(reader.GetString(1)),
                Text = reader.GetString(2),
                Model = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Language = reader.IsDBNull(4) ? null : reader.GetString(4),
                UsageType = reader.IsDBNull(5) ? null : reader.GetString(5),
                InputTokens = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                AudioTokens = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                OutputTokens = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                DurationSeconds = reader.IsDBNull(9) ? null : reader.GetInt32(9),
            });
        }

        return entries;
    }

    public void DeleteEntry(string id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM transcriptions WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);
        command.ExecuteNonQuery();
        Changed?.Invoke();
    }

    public void DeleteEntriesByDate(DateTime date)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM transcriptions WHERE date(timestamp, 'localtime') = @date";
        command.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));
        command.ExecuteNonQuery();
        Changed?.Invoke();
    }

    public void DeleteAllEntries()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM transcriptions";
        command.ExecuteNonQuery();
        Changed?.Invoke();
    }
}
