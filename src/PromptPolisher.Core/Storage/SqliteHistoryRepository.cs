using System.Globalization;
using Microsoft.Data.Sqlite;
using PromptPolisher.Core.Abstractions;
using PromptPolisher.Core.Models;

namespace PromptPolisher.Core.Storage;

/// <summary>
/// SQLite 历史记录。数据库放在 LocalAppData，不写日志、不写配置文件。
/// </summary>
public sealed class SqliteHistoryRepository : IHistoryRepository
{
    private readonly string _connectionString;

    public SqliteHistoryRepository(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS History (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CreatedAt TEXT NOT NULL,
                ProviderId TEXT NOT NULL,
                Model TEXT NOT NULL,
                ParametersJson TEXT NOT NULL,
                OriginalPrompt TEXT NOT NULL,
                PolishedPrompt TEXT NOT NULL,
                Status TEXT NOT NULL,
                ErrorCode TEXT NULL,
                DurationMs INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_History_Id_Desc ON History(Id DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> AddAsync(HistoryEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO History
                (CreatedAt, ProviderId, Model, ParametersJson, OriginalPrompt, PolishedPrompt, Status, ErrorCode, DurationMs)
            VALUES
                (@createdAt, @providerId, @model, @parametersJson, @originalPrompt, @polishedPrompt, @status, @errorCode, @durationMs);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("@createdAt", entry.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@providerId", entry.ProviderId);
        command.Parameters.AddWithValue("@model", entry.Model);
        command.Parameters.AddWithValue("@parametersJson", entry.ParametersJson);
        command.Parameters.AddWithValue("@originalPrompt", entry.OriginalPrompt);
        command.Parameters.AddWithValue("@polishedPrompt", entry.PolishedPrompt);
        command.Parameters.AddWithValue("@status", entry.Status.ToString());
        command.Parameters.AddWithValue("@errorCode", (object?)entry.ErrorCode ?? DBNull.Value);
        command.Parameters.AddWithValue("@durationMs", entry.DurationMs);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyList<HistoryEntry>> SearchAsync(
        string? query,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 500);
        var keyword = (query ?? string.Empty).Trim();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, CreatedAt, ProviderId, Model, ParametersJson, OriginalPrompt,
                   PolishedPrompt, Status, ErrorCode, DurationMs
            FROM History
            WHERE @q = ''
               OR OriginalPrompt LIKE @like
               OR PolishedPrompt LIKE @like
               OR ProviderId LIKE @like
               OR Model LIKE @like
            ORDER BY Id DESC
            LIMIT @limit;
            """;
        command.Parameters.AddWithValue("@q", keyword);
        command.Parameters.AddWithValue("@like", "%" + keyword + "%");
        command.Parameters.AddWithValue("@limit", limit);

        var entries = new List<HistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new HistoryEntry
            {
                Id = reader.GetInt64(0),
                CreatedAt = DateTimeOffset.Parse(
                    reader.GetString(1),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind),
                ProviderId = reader.GetString(2),
                Model = reader.GetString(3),
                ParametersJson = reader.GetString(4),
                OriginalPrompt = reader.GetString(5),
                PolishedPrompt = reader.GetString(6),
                Status = Enum.TryParse<HistoryStatus>(reader.GetString(7), ignoreCase: true, out var status)
                    ? status
                    : HistoryStatus.Failed,
                ErrorCode = reader.IsDBNull(8) ? null : reader.GetString(8),
                DurationMs = reader.GetInt64(9)
            });
        }

        return entries;
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM History WHERE Id = @id;";
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM History;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
