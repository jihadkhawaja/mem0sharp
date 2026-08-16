using System.Buffers.Binary;
using System.Globalization;
using System.Numerics.Tensors;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Mem0Sharp;

public sealed class SqliteMemoryStore : IMemoryStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string MemoryColumns = "id, text_value, user_id, agent_id, run_id, scope, metadata, created_at, updated_at, expires_at, hash_value, behavior, memory_type";
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
                hash_value TEXT NOT NULL DEFAULT '',
                behavior INTEGER NOT NULL DEFAULT 0,
                memory_type TEXT NULL
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

        await EnsureColumnAsync(connection, transaction, "memories", "behavior", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, transaction, "memories", "memory_type", "TEXT NULL", cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveAsync(Memory memory, IReadOnlyList<float>? embedding = null, CancellationToken cancellationToken = default)
    {
        if (embedding is not null) ValidateEmbedding(embedding);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var bytes = embedding is null ? null : SerializeEmbedding(embedding);
        await SaveMemoryAsync(connection, null, memory, bytes, cancellationToken);
    }

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

    public async Task SaveBatchAsync(IReadOnlyList<MemoryWriteRecord> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        foreach (var record in records)
        {
            if (record.Embedding is not null) ValidateEmbedding(record.Embedding);
        }
        if (records.Count == 0) return;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        foreach (var record in records)
        {
            var embedding = record.Embedding is null ? null : SerializeEmbedding(record.Embedding);
            await SaveMemoryAsync(connection, transaction, record.Memory, embedding, cancellationToken);
            if (record.History is not null)
            {
                await SaveHistoryAsync(connection, transaction, record.History, cancellationToken);
            }
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(string id, MemoryHistoryEntry? history = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await DeleteCoreAsync(connection, transaction, id, cancellationToken);
        if (history is not null)
        {
            await SaveHistoryAsync(connection, transaction, history, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> DeleteAllAsync(MemoryFilter? filter = null, IReadOnlyList<MemoryDeleteRecord>? records = null, CancellationToken cancellationToken = default)
    {
        if (records is not null)
        {
            if (records.Count == 0) return 0;
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            foreach (var record in records)
            {
                await DeleteCoreAsync(connection, transaction, record.Memory.Id, cancellationToken);
                await SaveHistoryAsync(connection, transaction, record.History, cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return records.Count;
        }

        var (where, parameters) = BuildFilter(filter);
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(cancellationToken);
        await using var command = conn.CreateCommand();
        command.Transaction = tx;
        command.CommandText = $"DELETE FROM memories {where}";
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return deleted;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(IReadOnlyList<float> embedding, MemoryFilter? filter = null, int topK = 5, CancellationToken cancellationToken = default)
    {
        ValidateEmbedding(embedding);
        if (topK < 0) throw new ArgumentOutOfRangeException(nameof(topK));
        if (topK == 0) return [];

        var queryArray = embedding.ToArray();
        var (where, parameters) = BuildFilter(filter);
        var embeddingCondition = string.IsNullOrEmpty(where) ? "WHERE embedding IS NOT NULL" : $"{where} AND embedding IS NOT NULL";

        var candidates = new List<(Memory Memory, float[] Embedding)>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {MemoryColumns}, embedding FROM memories {embeddingCondition} ORDER BY updated_at DESC";
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var memory = ReadMemory(reader);
            if (!MemoryFilterEvaluator.Matches(memory, filter)) continue;
            var vector = DeserializeEmbeddingArray((byte[])reader[13]);
            if (vector.Length != queryArray.Length) throw new InvalidDataException("SQLite contains an embedding with a different dimension than the query.");
            candidates.Add((memory, vector));
        }

        return candidates
            .Select(candidate => new SearchResult(candidate.Memory, (double)TensorPrimitives.CosineSimilarity(queryArray, candidate.Embedding)))
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
        var (where, parameters) = BuildFilter(filter);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {MemoryColumns} FROM memories {where} ORDER BY updated_at DESC";
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var memory = ReadMemory(reader);
            if (MemoryFilterEvaluator.Matches(memory, filter)) yield return memory;
        }
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

    public async Task<IReadOnlyList<MemoryHistoryEntry>> GetAllHistoryAsync(MemoryFilter? filter = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {HistoryColumns} FROM memory_history ORDER BY updated_at ASC, rowid ASC";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<MemoryHistoryEntry>();
        while (await reader.ReadAsync(cancellationToken)) entries.Add(ReadHistory(reader));
        return entries;
    }

    public async Task<RollbackResult> RollbackAsync(DateTimeOffset pointInTime, MemoryFilter? filter = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using var idsCommand = connection.CreateCommand();
        idsCommand.Transaction = transaction;
        idsCommand.CommandText = "SELECT DISTINCT memory_id FROM memory_history";
        var memoryIds = new List<string>();
        await using (var reader = await idsCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                memoryIds.Add(reader.GetString(0));
            }
        }

        var restored = 0;
        var deleted = 0;
        var affected = new HashSet<string>(StringComparer.Ordinal);
        var targetTimeStr = FormatTimestamp(pointInTime);

        foreach (var memoryId in memoryIds)
        {
            await using var historyCommand = connection.CreateCommand();
            historyCommand.Transaction = transaction;
            historyCommand.CommandText = $"SELECT {HistoryColumns} FROM memory_history WHERE memory_id = $id AND updated_at <= $target ORDER BY updated_at DESC, rowid DESC LIMIT 1";
            historyCommand.Parameters.AddWithValue("$id", memoryId);
            historyCommand.Parameters.AddWithValue("$target", targetTimeStr);

            MemoryHistoryEntry? lastEntry = null;
            await using (var reader = await historyCommand.ExecuteReaderAsync(cancellationToken))
            {
                if (await reader.ReadAsync(cancellationToken))
                {
                    lastEntry = ReadHistory(reader);
                }
            }

            if (lastEntry is null || lastEntry.IsDeleted || lastEntry.Event == MemoryHistoryEvent.Delete || string.IsNullOrEmpty(lastEntry.NewMemory))
            {
                await using var deleteCommand = connection.CreateCommand();
                deleteCommand.Transaction = transaction;
                deleteCommand.CommandText = "DELETE FROM memories WHERE id = $id";
                deleteCommand.Parameters.AddWithValue("$id", memoryId);
                var rows = await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
                if (rows > 0)
                {
                    deleted++;
                    affected.Add(memoryId);
                }
            }
            else
            {
                await using var checkCommand = connection.CreateCommand();
                checkCommand.Transaction = transaction;
                checkCommand.CommandText = "SELECT text_value FROM memories WHERE id = $id";
                checkCommand.Parameters.AddWithValue("$id", memoryId);
                var currentText = (string?)await checkCommand.ExecuteScalarAsync(cancellationToken);

                if (currentText is null)
                {
                    await using var insertCommand = connection.CreateCommand();
                    insertCommand.Transaction = transaction;
                    insertCommand.CommandText = """
                        INSERT INTO memories (id, text_value, user_id, agent_id, run_id, scope, metadata, created_at, updated_at, expires_at, hash_value, behavior, memory_type)
                        VALUES ($id, $text, $userId, $agentId, $runId, 0, '{}', $createdAt, $updatedAt, NULL, '', 0, NULL)
                        """;
                    insertCommand.Parameters.AddWithValue("$id", memoryId);
                    insertCommand.Parameters.AddWithValue("$text", lastEntry.NewMemory);
                    insertCommand.Parameters.AddWithValue("$userId", filter?.UserId ?? "default_user");
                    insertCommand.Parameters.AddWithValue("$agentId", (object?)filter?.AgentId ?? DBNull.Value);
                    insertCommand.Parameters.AddWithValue("$runId", (object?)filter?.RunId ?? DBNull.Value);
                    insertCommand.Parameters.AddWithValue("$createdAt", FormatTimestamp(lastEntry.CreatedAt));
                    insertCommand.Parameters.AddWithValue("$updatedAt", FormatTimestamp(lastEntry.UpdatedAt));
                    await insertCommand.ExecuteNonQueryAsync(cancellationToken);
                    restored++;
                    affected.Add(memoryId);
                }
                else if (currentText != lastEntry.NewMemory)
                {
                    await using var updateCommand = connection.CreateCommand();
                    updateCommand.Transaction = transaction;
                    updateCommand.CommandText = "UPDATE memories SET text_value = $text, updated_at = $updatedAt WHERE id = $id";
                    updateCommand.Parameters.AddWithValue("$id", memoryId);
                    updateCommand.Parameters.AddWithValue("$text", lastEntry.NewMemory);
                    updateCommand.Parameters.AddWithValue("$updatedAt", FormatTimestamp(lastEntry.UpdatedAt));
                    await updateCommand.ExecuteNonQueryAsync(cancellationToken);
                    restored++;
                    affected.Add(memoryId);
                }
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return new RollbackResult(restored, deleted, affected.ToArray());
    }

    public async Task<RollbackResult> RollbackToHistoryAsync(string historyEntryId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT updated_at FROM memory_history WHERE id = $id";
        command.Parameters.AddWithValue("$id", historyEntryId);
        var target = await command.ExecuteScalarAsync(cancellationToken);
        if (target is string timestampStr)
        {
            return await RollbackAsync(ParseTimestamp(timestampStr), cancellationToken: cancellationToken);
        }
        return new RollbackResult(0, 0, []);
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM memories; DELETE FROM memory_history;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static (string Where, List<(string Name, object Value)> Parameters) BuildFilter(MemoryFilter? filter, string prefix = "")
    {
        var conditions = new List<string>();
        var parameters = new List<(string, object)>();
        if (filter?.UserId is not null)
        {
            conditions.Add($"{prefix}user_id = $p{parameters.Count}");
            parameters.Add(($"$p{parameters.Count}", filter.UserId));
        }
        if (filter?.AgentId is not null)
        {
            conditions.Add($"{prefix}agent_id = $p{parameters.Count}");
            parameters.Add(($"$p{parameters.Count}", filter.AgentId));
        }
        if (filter?.RunId is not null)
        {
            conditions.Add($"{prefix}run_id = $p{parameters.Count}");
            parameters.Add(($"$p{parameters.Count}", filter.RunId));
        }
        if (filter?.Scope is not null)
        {
            conditions.Add($"{prefix}scope = $p{parameters.Count}");
            parameters.Add(($"$p{parameters.Count}", (int)filter.Scope.Value));
        }
        if (filter?.Behavior is not null)
        {
            conditions.Add($"{prefix}behavior = $p{parameters.Count}");
            parameters.Add(($"$p{parameters.Count}", (int)filter.Behavior.Value));
        }
        if (filter?.MemoryType is not null)
        {
            conditions.Add($"{prefix}memory_type = $p{parameters.Count}");
            parameters.Add(($"$p{parameters.Count}", filter.MemoryType));
        }
        if (filter?.IncludeExpired != true)
        {
            conditions.Add($"({prefix}expires_at IS NULL OR {prefix}expires_at > $p{parameters.Count})");
            parameters.Add(($"$p{parameters.Count}", DateTimeOffset.UtcNow.ToString("O")));
        }
        return (conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions), parameters);
    }

    private static async Task DeleteCoreAsync(SqliteConnection connection, SqliteTransaction? transaction, string id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM memories WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SaveMemoryAsync(SqliteConnection connection, SqliteTransaction? transaction, Memory memory, byte[]? embedding, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO memories (id, text_value, user_id, agent_id, run_id, scope, metadata, embedding, created_at, updated_at, expires_at, hash_value, behavior, memory_type)
            VALUES ($id, $text_value, $user_id, $agent_id, $run_id, $scope, $metadata, $embedding, $created_at, $updated_at, $expires_at, $hash_value, $behavior, $memory_type)
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
                hash_value = excluded.hash_value,
                behavior = excluded.behavior,
                memory_type = excluded.memory_type
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
        command.Parameters.AddWithValue("$behavior", (int)memory.Behavior);
        command.Parameters.AddWithValue("$memory_type", (object?)memory.MemoryType ?? DBNull.Value);
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
        Hash = reader.GetString(10),
        Behavior = reader.IsDBNull(11) ? MemoryBehavior.Normal : (MemoryBehavior)reader.GetInt32(11),
        MemoryType = reader.IsDBNull(12) ? null : reader.GetString(12)
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

    private static async Task EnsureColumnAsync(SqliteConnection connection, SqliteTransaction transaction, string tableName, string columnName, string definition, CancellationToken cancellationToken)
    {
        var exists = false;
        await using (var columnsCommand = connection.CreateCommand())
        {
            columnsCommand.Transaction = transaction;
            columnsCommand.CommandText = $"PRAGMA table_info({tableName})";
            await using var reader = await columnsCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }
        if (exists) return;

        await using var alterCommand = connection.CreateCommand();
        alterCommand.Transaction = transaction;
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition}";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
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

    private static float[] DeserializeEmbeddingArray(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length % sizeof(float) != 0) throw new InvalidDataException("SQLite contains an invalid embedding blob.");
        var embedding = new float[bytes.Length / sizeof(float)];
        for (var index = 0; index < embedding.Length; index++) embedding[index] = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(index * sizeof(float))));
        return embedding;
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