using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Mem0Sharp;

public sealed class SqliteMemoryStore : IBatchVectorMemoryStore, IBulkMemoryStore, IMemoryHistoryStore, IResettableMemoryStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string MemoryColumns = "id, text_value, user_id, agent_id, run_id, scope, metadata, created_at, updated_at, expires_at, hash_value";
    private const string HistoryColumns = "id, memory_id, event, old_memory, new_memory, created_at, updated_at, is_deleted, actor_id, role";
    private readonly string connectionString;

    public SqliteMemoryStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(connection, transaction, """
            CREATE TABLE IF NOT EXISTS memories (
                id TEXT PRIMARY KEY,
                text_value TEXT NOT NULL,
                user_id TEXT NOT NULL,
                agent_id TEXT NULL,
                run_id TEXT NULL,
                scope INTEGER NOT NULL,
                metadata TEXT NOT NULL,
                embedding BLOB NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                expires_at TEXT NULL,
                hash_value TEXT NOT NULL DEFAULT ''
            );
            CREATE TABLE IF NOT EXISTS memory_history (
                id TEXT PRIMARY KEY,
                memory_id TEXT NOT NULL,
                event INTEGER NOT NULL,
                old_memory TEXT NULL,
                new_memory TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                is_deleted INTEGER NOT NULL DEFAULT 0,
                actor_id TEXT NULL,
                role TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS memories_user_idx ON memories(user_id);
            CREATE INDEX IF NOT EXISTS memories_updated_at_idx ON memories(updated_at DESC);
            CREATE INDEX IF NOT EXISTS memory_history_memory_idx ON memory_history(memory_id, created_at);
            """, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveAsync(Memory memory, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await SaveMemoryAsync(connection, null, memory, null, cancellationToken);
    }

    public Task SaveAsync(Memory memory, IReadOnlyList<float> embedding, CancellationToken cancellationToken = default) =>
        SaveVectorAsync(memory, embedding, cancellationToken);

    public async Task SaveBatchAsync(IReadOnlyList<MemoryVectorRecord> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        ValidateBatch(records);
        if (records.Count == 0) return;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        foreach (var record in records)
        {
            await SaveMemoryAsync(connection, transaction, record.Memory, SerializeEmbedding(record.Embedding), cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(IReadOnlyList<float> embedding, MemoryFilter? filter = null, int topK = 5, CancellationToken cancellationToken = default)
    {
        ValidateEmbedding(embedding);
        if (topK < 0) throw new ArgumentOutOfRangeException(nameof(topK));
        if (topK == 0) return [];

        var candidates = new List<(Memory Memory, IReadOnlyList<float> Embedding)>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {MemoryColumns}, embedding FROM memories WHERE embedding IS NOT NULL ORDER BY updated_at DESC";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var memory = ReadMemory(reader);
            if (!MemoryFilterEvaluator.Matches(memory, filter)) continue;
            var vector = DeserializeEmbedding((byte[])reader[11]);
            if (vector.Count != embedding.Count) throw new InvalidDataException("SQLite contains an embedding with a different dimension than the query.");
            candidates.Add((memory, vector));
        }

        return candidates
            .Select(candidate => new SearchResult(candidate.Memory, CosineSimilarity(embedding, candidate.Embedding)))
            .OrderByDescending(result => result.Score)
            .Take(topK)
            .ToArray();
    }

    public async Task<Memory?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {MemoryColumns} FROM memories WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadMemory(reader) : null;
    }

    public async IAsyncEnumerable<Memory> GetAllAsync(MemoryFilter? filter = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var memories = await LoadAllAsync(cancellationToken);
        foreach (var memory in memories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (MemoryFilterEvaluator.Matches(memory, filter)) yield return memory;
        }
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM memories WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> DeleteAllAsync(MemoryFilter? filter = null, CancellationToken cancellationToken = default)
    {
        var memories = new List<Memory>();
        await foreach (var memory in GetAllAsync(filter, cancellationToken)) memories.Add(memory);
        if (memories.Count == 0) return 0;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        foreach (var memory in memories)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM memories WHERE id = $id";
            command.Parameters.AddWithValue("$id", memory.Id);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return memories.Count;
    }

    public async Task SaveHistoryAsync(MemoryHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await SaveHistoryAsync(connection, null, entry, cancellationToken);
    }

    public async Task<IReadOnlyList<MemoryHistoryEntry>> GetHistoryAsync(string memoryId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {HistoryColumns} FROM memory_history WHERE memory_id = $memory_id ORDER BY created_at, updated_at, rowid";
        command.Parameters.AddWithValue("$memory_id", memoryId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<MemoryHistoryEntry>();
        while (await reader.ReadAsync(cancellationToken)) entries.Add(ReadHistory(reader));
        return entries;
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM memories; DELETE FROM memory_history;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<List<Memory>> LoadAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {MemoryColumns} FROM memories ORDER BY updated_at DESC";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var memories = new List<Memory>();
        while (await reader.ReadAsync(cancellationToken)) memories.Add(ReadMemory(reader));
        return memories;
    }

    private async Task SaveVectorAsync(Memory memory, IReadOnlyList<float> embedding, CancellationToken cancellationToken)
    {
        ValidateEmbedding(embedding);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await SaveMemoryAsync(connection, null, memory, SerializeEmbedding(embedding), cancellationToken);
    }

    private static async Task SaveMemoryAsync(SqliteConnection connection, SqliteTransaction? transaction, Memory memory, byte[]? embedding, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO memories (id, text_value, user_id, agent_id, run_id, scope, metadata, embedding, created_at, updated_at, expires_at, hash_value)
            VALUES ($id, $text_value, $user_id, $agent_id, $run_id, $scope, $metadata, $embedding, $created_at, $updated_at, $expires_at, $hash_value)
            ON CONFLICT(id) DO UPDATE SET
                text_value = excluded.text_value,
                user_id = excluded.user_id,
                agent_id = excluded.agent_id,
                run_id = excluded.run_id,
                scope = excluded.scope,
                metadata = excluded.metadata,
                embedding = COALESCE(excluded.embedding, memories.embedding),
                created_at = excluded.created_at,
                updated_at = excluded.updated_at,
                expires_at = excluded.expires_at,
                hash_value = excluded.hash_value
            """;
        command.Parameters.AddWithValue("$id", memory.Id);
        command.Parameters.AddWithValue("$text_value", memory.Text);
        command.Parameters.AddWithValue("$user_id", memory.UserId);
        command.Parameters.AddWithValue("$agent_id", (object?)memory.AgentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$run_id", (object?)memory.RunId ?? DBNull.Value);
        command.Parameters.AddWithValue("$scope", (int)memory.Scope);
        command.Parameters.AddWithValue("$metadata", JsonSerializer.Serialize(memory.Metadata, JsonOptions));
        command.Parameters.Add("$embedding", SqliteType.Blob).Value = (object?)embedding ?? DBNull.Value;
        command.Parameters.AddWithValue("$created_at", FormatTimestamp(memory.CreatedAt));
        command.Parameters.AddWithValue("$updated_at", FormatTimestamp(memory.UpdatedAt));
        command.Parameters.AddWithValue("$expires_at", (object?)(memory.ExpiresAt is null ? null : FormatTimestamp(memory.ExpiresAt.Value)) ?? DBNull.Value);
        command.Parameters.AddWithValue("$hash_value", memory.Hash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SaveHistoryAsync(SqliteConnection connection, SqliteTransaction? transaction, MemoryHistoryEntry entry, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO memory_history (id, memory_id, event, old_memory, new_memory, created_at, updated_at, is_deleted, actor_id, role)
            VALUES ($id, $memory_id, $event, $old_memory, $new_memory, $created_at, $updated_at, $is_deleted, $actor_id, $role)
            """;
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$memory_id", entry.MemoryId);
        command.Parameters.AddWithValue("$event", (int)entry.Event);
        command.Parameters.AddWithValue("$old_memory", (object?)entry.OldMemory ?? DBNull.Value);
        command.Parameters.AddWithValue("$new_memory", (object?)entry.NewMemory ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at", FormatTimestamp(entry.CreatedAt));
        command.Parameters.AddWithValue("$updated_at", FormatTimestamp(entry.UpdatedAt));
        command.Parameters.AddWithValue("$is_deleted", entry.IsDeleted);
        command.Parameters.AddWithValue("$actor_id", (object?)entry.ActorId ?? DBNull.Value);
        command.Parameters.AddWithValue("$role", (object?)entry.Role ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Memory ReadMemory(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Text = reader.GetString(1),
        UserId = reader.GetString(2),
        AgentId = reader.IsDBNull(3) ? null : reader.GetString(3),
        RunId = reader.IsDBNull(4) ? null : reader.GetString(4),
        Scope = (MemoryScope)reader.GetInt32(5),
        Metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(6), JsonOptions) ?? new(),
        CreatedAt = ParseTimestamp(reader.GetString(7)),
        UpdatedAt = ParseTimestamp(reader.GetString(8)),
        ExpiresAt = reader.IsDBNull(9) ? null : ParseTimestamp(reader.GetString(9)),
        Hash = reader.GetString(10)
    };

    private static MemoryHistoryEntry ReadHistory(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        MemoryId = reader.GetString(1),
        Event = (MemoryHistoryEvent)reader.GetInt32(2),
        OldMemory = reader.IsDBNull(3) ? null : reader.GetString(3),
        NewMemory = reader.IsDBNull(4) ? null : reader.GetString(4),
        CreatedAt = ParseTimestamp(reader.GetString(5)),
        UpdatedAt = ParseTimestamp(reader.GetString(6)),
        IsDeleted = reader.GetBoolean(7),
        ActorId = reader.IsDBNull(8) ? null : reader.GetString(8),
        Role = reader.IsDBNull(9) ? null : reader.GetString(9)
    };

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void ValidateBatch(IReadOnlyList<MemoryVectorRecord> records)
    {
        var dimensions = records.Count == 0 ? 0 : records[0].Embedding.Count;
        foreach (var record in records)
        {
            ValidateEmbedding(record.Embedding);
            if (record.Embedding.Count != dimensions) throw new ArgumentException("SQLite vector records must have consistent embedding dimensions.", nameof(records));
        }
    }

    private static void ValidateEmbedding(IReadOnlyList<float> embedding)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        if (embedding.Count == 0) throw new ArgumentException("Embedding must contain at least one value.", nameof(embedding));
        if (embedding.Any(value => !float.IsFinite(value))) throw new ArgumentException("Embedding values must be finite.", nameof(embedding));
    }

    private static byte[] SerializeEmbedding(IReadOnlyList<float> embedding)
    {
        var bytes = new byte[embedding.Count * sizeof(float)];
        for (var index = 0; index < embedding.Count; index++) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * sizeof(float)), BitConverter.SingleToInt32Bits(embedding[index]));
        return bytes;
    }

    private static IReadOnlyList<float> DeserializeEmbedding(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length % sizeof(float) != 0) throw new InvalidDataException("SQLite contains an invalid embedding blob.");
        var embedding = new float[bytes.Length / sizeof(float)];
        for (var index = 0; index < embedding.Length; index++) embedding[index] = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(index * sizeof(float))));
        return embedding;
    }

    private static double CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        double dot = 0;
        double leftMagnitude = 0;
        double rightMagnitude = 0;
        for (var index = 0; index < left.Count; index++)
        {
            dot += left[index] * right[index];
            leftMagnitude += left[index] * left[index];
            rightMagnitude += right[index] * right[index];
        }
        return leftMagnitude == 0 || rightMagnitude == 0 ? 0 : dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
    }

    private static string FormatTimestamp(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}