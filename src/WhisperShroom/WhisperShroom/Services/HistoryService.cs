using Microsoft.Data.Sqlite;
using WhisperShroom.Models;

namespace WhisperShroom.Services;

public sealed class HistoryService
{
    private readonly string _connectionString;
    private readonly string _audioDir;

    /// <summary>
    /// Raised when the history data changes (entry added or deleted).
    /// </summary>
    public event Action? Changed;

    public HistoryService()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WhisperShroom");

        var dbPath = Path.Combine(appDataDir, "history.db");
        _connectionString = $"Data Source={dbPath}";
        _audioDir = Path.Combine(appDataDir, "audio");
    }

    public string AudioDir => _audioDir;

    public void InitializeDatabase()
    {
        var dir = Path.GetDirectoryName(_connectionString.Replace("Data Source=", ""))!;
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(_audioDir);

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
                duration_seconds INTEGER,
                status TEXT NOT NULL DEFAULT 'completed',
                audio_path TEXT,
                error_message TEXT
            )
            """;
        command.ExecuteNonQuery();

        // Migration: add columns if upgrading from older schema
        MigrateSchema(connection);
    }

    private static void MigrateSchema(SqliteConnection connection)
    {
        var columns = new HashSet<string>();
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA table_info(transcriptions)";
            using var reader = pragma.ExecuteReader();
            while (reader.Read())
                columns.Add(reader.GetString(1));
        }

        if (!columns.Contains("status"))
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "ALTER TABLE transcriptions ADD COLUMN status TEXT NOT NULL DEFAULT 'completed'";
            cmd.ExecuteNonQuery();
        }

        if (!columns.Contains("audio_path"))
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "ALTER TABLE transcriptions ADD COLUMN audio_path TEXT";
            cmd.ExecuteNonQuery();
        }

        if (!columns.Contains("error_message"))
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "ALTER TABLE transcriptions ADD COLUMN error_message TEXT";
            cmd.ExecuteNonQuery();
        }
    }

    public void AddEntry(TranscriptionResult result, string? model, string? language)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO transcriptions (id, timestamp, text, model, language, usage_type, input_tokens, audio_tokens, output_tokens, duration_seconds, status)
            VALUES (@id, @timestamp, @text, @model, @language, @usageType, @inputTokens, @audioTokens, @outputTokens, @durationSeconds, 'completed')
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

    public string AddPendingEntry(string audioPath, string? model, string? language, string errorMessage)
    {
        var id = Guid.NewGuid().ToString();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO transcriptions (id, timestamp, text, model, language, status, audio_path, error_message)
            VALUES (@id, @timestamp, '', @model, @language, 'pending', @audioPath, @errorMessage)
            """;

        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@timestamp", DateTimeOffset.Now.ToString("o"));
        command.Parameters.AddWithValue("@model", (object?)model ?? DBNull.Value);
        command.Parameters.AddWithValue("@language", (object?)language ?? DBNull.Value);
        command.Parameters.AddWithValue("@audioPath", audioPath);
        command.Parameters.AddWithValue("@errorMessage", errorMessage);

        command.ExecuteNonQuery();
        Changed?.Invoke();

        return id;
    }

    public void CompletePendingEntry(string id, TranscriptionResult result)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE transcriptions
            SET text = @text,
                status = 'completed',
                audio_path = NULL,
                error_message = NULL,
                usage_type = @usageType,
                input_tokens = @inputTokens,
                audio_tokens = @audioTokens,
                output_tokens = @outputTokens,
                duration_seconds = @durationSeconds
            WHERE id = @id
            """;

        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@text", result.Text);
        command.Parameters.AddWithValue("@usageType", (object?)result.UsageType ?? DBNull.Value);
        command.Parameters.AddWithValue("@inputTokens", (object?)result.InputTokens ?? DBNull.Value);
        command.Parameters.AddWithValue("@audioTokens", (object?)result.AudioTokens ?? DBNull.Value);
        command.Parameters.AddWithValue("@outputTokens", (object?)result.OutputTokens ?? DBNull.Value);
        command.Parameters.AddWithValue("@durationSeconds", (object?)result.DurationSeconds ?? DBNull.Value);

        command.ExecuteNonQuery();
        Changed?.Invoke();
    }

    public void UpdatePendingError(string id, string errorMessage)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE transcriptions SET error_message = @errorMessage WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@errorMessage", errorMessage);
        command.ExecuteNonQuery();
        Changed?.Invoke();
    }

    public List<TranscriptionEntry> GetAllEntries()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, timestamp, text, model, language, usage_type, input_tokens, audio_tokens, output_tokens, duration_seconds, status, audio_path, error_message
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
                Status = reader.IsDBNull(10) ? "completed" : reader.GetString(10),
                AudioPath = reader.IsDBNull(11) ? null : reader.GetString(11),
                ErrorMessage = reader.IsDBNull(12) ? null : reader.GetString(12),
            });
        }

        return entries;
    }

    public void DeleteEntry(string id)
    {
        CleanupAudioForEntries(id);

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
        CleanupAudioForDate(date);

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
        CleanupAllAudio();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM transcriptions";
        command.ExecuteNonQuery();
        Changed?.Invoke();
    }

    private void CleanupAudioForEntries(params string[] ids)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        foreach (var id in ids)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT audio_path FROM transcriptions WHERE id = @id AND audio_path IS NOT NULL";
            command.Parameters.AddWithValue("@id", id);
            var path = command.ExecuteScalar() as string;
            if (path is not null)
                TryDeleteFile(path);
        }
    }

    private void CleanupAudioForDate(DateTime date)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT audio_path FROM transcriptions WHERE date(timestamp, 'localtime') = @date AND audio_path IS NOT NULL";
        command.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));
        using var reader = command.ExecuteReader();
        while (reader.Read())
            TryDeleteFile(reader.GetString(0));
    }

    private void CleanupAllAudio()
    {
        try
        {
            if (Directory.Exists(_audioDir))
            {
                foreach (var file in Directory.GetFiles(_audioDir, "*.wav"))
                    TryDeleteFile(file);
            }
        }
        catch { }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
