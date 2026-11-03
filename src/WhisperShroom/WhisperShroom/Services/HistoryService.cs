using System.Globalization;
using Microsoft.Data.Sqlite;
using WhisperShroom.Helpers;
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

        // Write-ahead logging lets the History window read on the thread pool while a
        // transcription writes, instead of readers and the writer blocking each other.
        // Recommended by the Microsoft.Data.Sqlite docs for concurrent access; the mode is
        // stored in the database file, so setting it on every start is idempotent.
        using (var wal = connection.CreateCommand())
        {
            wal.CommandText = "PRAGMA journal_mode = 'wal'";
            wal.ExecuteNonQuery();
        }

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

    public string AddPendingEntry(string audioPath, string? model, string? language, string errorMessage)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var id = InsertPendingEntry(connection, DateTimeOffset.Now, audioPath, model, language, errorMessage);
        Changed?.Invoke();
        return id;
    }

    private static string InsertPendingEntry(
        SqliteConnection connection, DateTimeOffset timestamp, string audioPath,
        string? model, string? language, string errorMessage)
    {
        var id = Guid.NewGuid().ToString();

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO transcriptions (id, timestamp, text, model, language, status, audio_path, error_message)
            VALUES (@id, @timestamp, '', @model, @language, 'pending', @audioPath, @errorMessage)
            """;

        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@timestamp", timestamp.ToString("o"));
        command.Parameters.AddWithValue("@model", (object?)model ?? DBNull.Value);
        command.Parameters.AddWithValue("@language", (object?)language ?? DBNull.Value);
        command.Parameters.AddWithValue("@audioPath", audioPath);
        command.Parameters.AddWithValue("@errorMessage", errorMessage);

        command.ExecuteNonQuery();
        return id;
    }

    public const string InterruptedRecordingMessage = "Recording was interrupted. Retry to transcribe.";

    /// <summary>
    /// Turns WAV files in the audio folder that no history entry references into pending
    /// entries. Such files exist when the app ended while recording (crash, forced exit), since
    /// the recording streams to disk and the entry is only written after the recording stops.
    /// Files without any audio data are deleted. Must run before the first recording starts.
    /// </summary>
    public void RecoverOrphanedRecordings()
    {
        if (!Directory.Exists(_audioDir))
            return;

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT audio_path FROM transcriptions WHERE audio_path IS NOT NULL";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                referenced.Add(Path.GetFullPath(reader.GetString(0)));
        }

        var recovered = 0;
        foreach (var file in Directory.EnumerateFiles(_audioDir, "*.wav"))
        {
            if (referenced.Contains(Path.GetFullPath(file)))
                continue;

            long? dataBytes;
            try
            {
                dataBytes = WavFileRepair.RepairHeader(file);
            }
            catch (IOException)
            {
                continue; // locked or unreadable; try again on the next start
            }

            if (dataBytes is null or 0)
            {
                TryDeleteFile(file);
                continue;
            }

            InsertPendingEntry(connection, new DateTimeOffset(File.GetCreationTime(file)),
                file, model: null, language: null, InterruptedRecordingMessage);
            recovered++;
        }

        if (recovered > 0)
            Changed?.Invoke();
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

    // --- Read side for the History window ---------------------------------------------------
    //
    // Periods are derived with SQLite's 'localtime' modifier, the same rule DeleteEntriesByDate
    // already relies on, so a day's header, its entries and "Delete this day" always agree.

    private const string EntryColumns =
        "id, timestamp, text, model, language, usage_type, input_tokens, audio_tokens, output_tokens, duration_seconds, status, audio_path, error_message";

    // Per period, model and usage type. Cost is computed in C# by CostCalculator (single source
    // of pricing) from the summed usage; the cost_* sums mirror CalculateCostEur's per-entry
    // rules: completed entries only, token billing requires input_tokens to be present.
    private const string UsageAggregateColumns = """
        model, usage_type,
        COUNT(*) AS entry_count,
        SUM(status = 'pending') AS pending_count,
        SUM(CASE WHEN status = 'completed' AND usage_type = 'tokens'
                 THEN COALESCE(input_tokens, 0) + COALESCE(output_tokens, 0) ELSE 0 END) AS total_tokens,
        SUM(CASE WHEN status = 'completed' AND usage_type = 'duration'
                 THEN COALESCE(duration_seconds, 0) ELSE 0 END) AS duration_seconds,
        SUM(CASE WHEN status = 'completed' AND usage_type = 'tokens' AND input_tokens IS NOT NULL
                 THEN input_tokens ELSE 0 END) AS cost_input_tokens,
        SUM(CASE WHEN status = 'completed' AND usage_type = 'tokens' AND input_tokens IS NOT NULL
                 THEN COALESCE(output_tokens, 0) ELSE 0 END) AS cost_output_tokens
        """;

    /// <summary>One summary per local month, newest first.</summary>
    public List<HistoryPeriodSummary> GetMonthSummaries()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var dayCounts = new Dictionary<string, int>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT strftime('%Y-%m', timestamp, 'localtime') AS period,
                       COUNT(DISTINCT date(timestamp, 'localtime'))
                FROM transcriptions
                GROUP BY period
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
                dayCounts[reader.GetString(0)] = reader.GetInt32(1);
        }

        using var aggregate = connection.CreateCommand();
        aggregate.CommandText = $"""
            SELECT strftime('%Y-%m', timestamp, 'localtime') AS period, {UsageAggregateColumns}
            FROM transcriptions
            GROUP BY period, model, usage_type
            """;

        return ReadPeriodSummaries(aggregate, "yyyy-MM", dayCounts);
    }

    /// <summary>One summary per local day of the given month, newest first.</summary>
    public List<HistoryPeriodSummary> GetDaySummaries(int year, int month)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT date(timestamp, 'localtime') AS period, {UsageAggregateColumns}
            FROM transcriptions
            WHERE strftime('%Y-%m', timestamp, 'localtime') = @month
            GROUP BY period, model, usage_type
            """;
        command.Parameters.AddWithValue("@month", $"{year:D4}-{month:D2}");

        return ReadPeriodSummaries(command, "yyyy-MM-dd", dayCounts: null);
    }

    /// <summary>All entries of one local day, newest first.</summary>
    public List<TranscriptionEntry> GetEntriesForDay(DateTime date)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {EntryColumns}
            FROM transcriptions
            WHERE date(timestamp, 'localtime') = @date
            ORDER BY timestamp DESC
            """;
        command.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return ReadEntries(command);
    }

    /// <summary>
    /// Completed entries whose text contains <paramref name="query"/>, newest first, capped at
    /// <paramref name="limit"/>, plus the total number of matches.
    /// </summary>
    public (List<TranscriptionEntry> Results, int TotalCount) SearchEntries(string query, int limit)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // SQLite's LIKE is case-insensitive for ASCII only ("Ä" would not match "ä"). A
        // registered C# function keeps the exact OrdinalIgnoreCase semantics the in-memory
        // search had, while the filtering and the cap still happen inside the query.
        connection.CreateFunction(
            "contains_ci",
            (string text, string value) => text.Contains(value, StringComparison.OrdinalIgnoreCase),
            isDeterministic: true);

        int totalCount;
        using (var count = connection.CreateCommand())
        {
            count.CommandText = """
                SELECT COUNT(*) FROM transcriptions
                WHERE status = 'completed' AND contains_ci(text, @query)
                """;
            count.Parameters.AddWithValue("@query", query);
            totalCount = Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {EntryColumns}
            FROM transcriptions
            WHERE status = 'completed' AND contains_ci(text, @query)
            ORDER BY timestamp DESC
            LIMIT @limit
            """;
        command.Parameters.AddWithValue("@query", query);
        command.Parameters.AddWithValue("@limit", limit);

        return (ReadEntries(command), totalCount);
    }

    private static List<HistoryPeriodSummary> ReadPeriodSummaries(
        SqliteCommand command, string periodFormat, Dictionary<string, int>? dayCounts)
    {
        var periods = new Dictionary<string, (int Entries, int Pending, long Tokens, long Seconds, decimal Cost)>();

        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var period = reader.GetString(0);
                var model = reader.IsDBNull(1) ? null : reader.GetString(1);
                var usageType = reader.IsDBNull(2) ? null : reader.GetString(2);
                var durationSeconds = reader.GetInt64(6);

                var cost = CostCalculator.CalculateCostEur(
                    model, usageType,
                    inputTokens: reader.GetInt64(7),
                    outputTokens: reader.GetInt64(8),
                    durationSeconds: durationSeconds) ?? 0m;

                periods.TryGetValue(period, out var acc);
                periods[period] = (
                    acc.Entries + reader.GetInt32(3),
                    acc.Pending + reader.GetInt32(4),
                    acc.Tokens + reader.GetInt64(5),
                    acc.Seconds + durationSeconds,
                    acc.Cost + cost);
            }
        }

        return periods
            .Select(p => new HistoryPeriodSummary(
                DateTime.ParseExact(p.Key, periodFormat, CultureInfo.InvariantCulture),
                p.Value.Entries,
                p.Value.Pending,
                dayCounts?.GetValueOrDefault(p.Key) ?? 1,
                p.Value.Tokens,
                p.Value.Seconds,
                p.Value.Cost))
            .OrderByDescending(s => s.PeriodStart)
            .ToList();
    }

    private static List<TranscriptionEntry> ReadEntries(SqliteCommand command)
    {
        var entries = new List<TranscriptionEntry>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            entries.Add(new TranscriptionEntry
            {
                Id = reader.GetString(0),
                Timestamp = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
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
        command.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
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
        command.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
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
