using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;

namespace Mem0Sharp;

public sealed class PostgresMemoryStore : IBatchVectorMemoryStore, IBulkMemoryStore, IMemoryHistoryStore, IResettableMemoryStore, IAsyncDisposable
{
    private static readonly Regex IdentifierPattern = new("^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly PostgresMemoryStoreOptions options;
    private readonly string tableName;
    private readonly string historyTableName;

    public PostgresMemoryStore(PostgresMemoryStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.EmbeddingDimensions < 1) throw new ArgumentOutOfRangeException(nameof(options.EmbeddingDimensions));
        if (!IdentifierPattern.IsMatch(options.TableName)) throw new ArgumentException("TableName must be a simple PostgreSQL identifier.", nameof(options));
        this.options = options;
        tableName = $"\"{options.TableName}\"";
        historyTableName = $"\"{options.TableName}_history\"";
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        if (options.CreateExtension) await ExecuteAsync(connection, "CREATE EXTENSION IF NOT EXISTS vector", cancellationToken);
        var indexSql = options.UseHnswIndex && options.EmbeddingDimensions <= 2000
            ? $"CREATE INDEX IF NOT EXISTS \"{options.TableName}_embedding_hnsw_idx\" ON {tableName} USING hnsw (embedding vector_cosine_ops);"
            : string.Empty;
        await ExecuteAsync(connection, $"""
            CREATE TABLE IF NOT EXISTS {tableName} (
                id text PRIMARY KEY,
                text_value text NOT NULL,
                user_id text NOT NULL,
                agent_id text NULL,
                run_id text NULL,
                scope integer NOT NULL,
                metadata jsonb NOT NULL,
                embedding vector({options.EmbeddingDimensions}) NULL,
                created_at timestamptz NOT NULL,
                updated_at timestamptz NOT NULL,
                expires_at timestamptz NULL,
                hash_value text NOT NULL DEFAULT ''
            );
            ALTER TABLE {tableName} ADD COLUMN IF NOT EXISTS expires_at timestamptz NULL;
            ALTER TABLE {tableName} ADD COLUMN IF NOT EXISTS hash_value text NOT NULL DEFAULT '';
            CREATE INDEX IF NOT EXISTS "{options.TableName}_user_idx" ON {tableName} (user_id);
            CREATE TABLE IF NOT EXISTS {historyTableName} (
                id text PRIMARY KEY,
                memory_id text NOT NULL,
                event integer NOT NULL,
                old_memory text NULL,
                new_memory text NULL,
                created_at timestamptz NOT NULL,
                updated_at timestamptz NOT NULL,
                is_deleted boolean NOT NULL DEFAULT false,
                actor_id text NULL,
                role text NULL
            );
            ALTER TABLE {historyTableName} ADD COLUMN IF NOT EXISTS updated_at timestamptz NULL;
            ALTER TABLE {historyTableName} ADD COLUMN IF NOT EXISTS is_deleted boolean NOT NULL DEFAULT false;
            ALTER TABLE {historyTableName} ADD COLUMN IF NOT EXISTS actor_id text NULL;
            ALTER TABLE {historyTableName} ADD COLUMN IF NOT EXISTS role text NULL;
            UPDATE {historyTableName} SET updated_at = created_at WHERE updated_at IS NULL;
            ALTER TABLE {historyTableName} ALTER COLUMN updated_at SET NOT NULL;
            CREATE INDEX IF NOT EXISTS "{options.TableName}_history_memory_idx" ON {historyTableName} (memory_id, created_at);
            {indexSql}
            """, cancellationToken);
    }

    public async Task SaveAsync(Memory memory, CancellationToken cancellationToken = default)
    {
        await SaveCoreAsync(memory, null, cancellationToken);
    }

    public async Task SaveAsync(Memory memory, IReadOnlyList<float> embedding, CancellationToken cancellationToken = default)
    {
        if (embedding.Count != options.EmbeddingDimensions) throw new ArgumentException("Embedding dimensions do not match the PostgreSQL vector column.", nameof(embedding));
        await SaveCoreAsync(memory, embedding, cancellationToken);
    }

    public async Task SaveBatchAsync(IReadOnlyList<MemoryVectorRecord> records, CancellationToken cancellationToken = default)
    {
        foreach (var record in records)
        {
            if (record.Embedding.Count != options.EmbeddingDimensions) throw new ArgumentException("Embedding dimensions do not match the PostgreSQL vector column.", nameof(records));
        }
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var record in records) await SaveCoreAsync(connection, transaction, record.Memory, record.Embedding, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<Memory?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"SELECT id, text_value, user_id, agent_id, run_id, scope, metadata, created_at, updated_at, expires_at, hash_value FROM {tableName} WHERE id = $1", connection);
        command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadMemory(reader) : null;
    }

    public async IAsyncEnumerable<Memory> GetAllAsync(MemoryFilter? filter = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var (where, parameters) = BuildFilter(filter);
        await using var command = new NpgsqlCommand($"SELECT id, text_value, user_id, agent_id, run_id, scope, metadata, created_at, updated_at, expires_at, hash_value FROM {tableName} {where} ORDER BY updated_at DESC", connection);
        AddParameters(command, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) yield return ReadMemory(reader);
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(IReadOnlyList<float> embedding, MemoryFilter? filter = null, int topK = 5, CancellationToken cancellationToken = default)
    {
        if (embedding.Count != options.EmbeddingDimensions) throw new ArgumentException("Embedding dimensions do not match the PostgreSQL vector column.", nameof(embedding));
        if (topK < 0) throw new ArgumentOutOfRangeException(nameof(topK));
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var (where, parameters) = BuildFilter(filter, "m.", 2);
        var embeddingCondition = string.IsNullOrEmpty(where) ? "WHERE m.embedding IS NOT NULL" : $"{where} AND m.embedding IS NOT NULL";
        var topKParameter = parameters.Count + 2;
        await using var command = new NpgsqlCommand($"SELECT m.id, m.text_value, m.user_id, m.agent_id, m.run_id, m.scope, m.metadata, m.created_at, m.updated_at, m.expires_at, m.hash_value, 1 - (m.embedding <=> $1::vector) AS score FROM {tableName} m {embeddingCondition} ORDER BY m.embedding <=> $1::vector LIMIT ${topKParameter}", connection);
        command.Parameters.AddWithValue(ToVectorLiteral(embedding));
        AddParameters(command, parameters);
        command.Parameters.AddWithValue(topK);
        var results = new List<SearchResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var score = reader.GetDouble(11);
            results.Add(new SearchResult(ReadMemory(reader), score, new SearchScoreDetails(score)));
        }
        return results;
    }

    public async Task<IReadOnlyList<IReadOnlyList<SearchResult>>> SearchBatchAsync(IReadOnlyList<IReadOnlyList<float>> embeddings, MemoryFilter? filter = null, int topK = 5, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(embeddings);
        var searches = embeddings.Select(embedding => SearchAsync(embedding, filter, topK, cancellationToken));
        return await Task.WhenAll(searches);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"DELETE FROM {tableName} WHERE id = $1", connection);
        command.Parameters.AddWithValue(id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> DeleteAllAsync(MemoryFilter? filter = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var (where, parameters) = BuildFilter(filter);
        await using var command = new NpgsqlCommand($"DELETE FROM {tableName} {where}", connection);
        AddParameters(command, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveHistoryAsync(MemoryHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"INSERT INTO {historyTableName} (id, memory_id, event, old_memory, new_memory, created_at, updated_at, is_deleted, actor_id, role) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)", connection);
        command.Parameters.AddWithValue(entry.Id);
        command.Parameters.AddWithValue(entry.MemoryId);
        command.Parameters.AddWithValue((int)entry.Event);
        command.Parameters.AddWithValue((object?)entry.OldMemory ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)entry.NewMemory ?? DBNull.Value);
        command.Parameters.AddWithValue(entry.CreatedAt);
        command.Parameters.AddWithValue(entry.UpdatedAt);
        command.Parameters.AddWithValue(entry.IsDeleted);
        command.Parameters.AddWithValue((object?)entry.ActorId ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)entry.Role ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MemoryHistoryEntry>> GetHistoryAsync(string memoryId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"SELECT id, memory_id, event, old_memory, new_memory, created_at, updated_at, is_deleted, actor_id, role FROM {historyTableName} WHERE memory_id = $1 ORDER BY created_at, updated_at, id", connection);
        command.Parameters.AddWithValue(memoryId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<MemoryHistoryEntry>();
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new MemoryHistoryEntry
            {
                Id = reader.GetString(0),
                MemoryId = reader.GetString(1),
                Event = (MemoryHistoryEvent)reader.GetInt32(2),
                OldMemory = reader.IsDBNull(3) ? null : reader.GetString(3),
                NewMemory = reader.IsDBNull(4) ? null : reader.GetString(4),
                CreatedAt = reader.GetFieldValue<DateTimeOffset>(5),
                UpdatedAt = reader.GetFieldValue<DateTimeOffset>(6),
                IsDeleted = reader.GetBoolean(7),
                ActorId = reader.IsDBNull(8) ? null : reader.GetString(8),
                Role = reader.IsDBNull(9) ? null : reader.GetString(9)
            });
        }
        return entries;
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await ExecuteAsync(connection, $"TRUNCATE TABLE {tableName}, {historyTableName}", cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task SaveCoreAsync(Memory memory, IReadOnlyList<float>? embedding, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await SaveCoreAsync(connection, null, memory, embedding, cancellationToken);
    }

    private async Task SaveCoreAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Memory memory, IReadOnlyList<float>? embedding, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"""
            INSERT INTO {tableName} (id, text_value, user_id, agent_id, run_id, scope, metadata, embedding, created_at, updated_at, expires_at, hash_value)
            VALUES ($1, $2, $3, $4, $5, $6, $7::jsonb, $8::vector, $9, $10, $11, $12)
            ON CONFLICT (id) DO UPDATE SET text_value = EXCLUDED.text_value, user_id = EXCLUDED.user_id, agent_id = EXCLUDED.agent_id, run_id = EXCLUDED.run_id, scope = EXCLUDED.scope, metadata = EXCLUDED.metadata, embedding = COALESCE(EXCLUDED.embedding, {tableName}.embedding), updated_at = EXCLUDED.updated_at, expires_at = EXCLUDED.expires_at, hash_value = EXCLUDED.hash_value
            """, connection, transaction);
        command.Parameters.AddWithValue(memory.Id);
        command.Parameters.AddWithValue(memory.Text);
        command.Parameters.AddWithValue(memory.UserId);
        command.Parameters.AddWithValue((object?)memory.AgentId ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)memory.RunId ?? DBNull.Value);
        command.Parameters.AddWithValue((int)memory.Scope);
        command.Parameters.AddWithValue(JsonSerializer.Serialize(memory.Metadata));
        command.Parameters.AddWithValue((object?)(embedding is null ? null : ToVectorLiteral(embedding)) ?? DBNull.Value);
        command.Parameters.AddWithValue(memory.CreatedAt);
        command.Parameters.AddWithValue(memory.UpdatedAt);
        command.Parameters.AddWithValue((object?)memory.ExpiresAt ?? DBNull.Value);
        command.Parameters.AddWithValue(memory.Hash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Memory ReadMemory(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetString(0), Text = reader.GetString(1), UserId = reader.GetString(2), AgentId = reader.IsDBNull(3) ? null : reader.GetString(3), RunId = reader.IsDBNull(4) ? null : reader.GetString(4), Scope = (MemoryScope)reader.GetInt32(5), Metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(6)) ?? new(), CreatedAt = reader.GetFieldValue<DateTimeOffset>(7), UpdatedAt = reader.GetFieldValue<DateTimeOffset>(8), ExpiresAt = reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9), Hash = reader.GetString(10)
    };

    private static (string Where, List<(string Name, object Value)> Parameters) BuildFilter(MemoryFilter? filter, string prefix = "", int startIndex = 1)
    {
        var conditions = new List<string>();
        var parameters = new List<(string, object)>();
        AddFilter(filter?.UserId, $"{prefix}user_id", "user_id", conditions, parameters, startIndex);
        AddFilter(filter?.AgentId, $"{prefix}agent_id", "agent_id", conditions, parameters, startIndex);
        AddFilter(filter?.RunId, $"{prefix}run_id", "run_id", conditions, parameters, startIndex);
        if (filter?.Scope is not null) { conditions.Add($"{prefix}scope = ${startIndex + parameters.Count}"); parameters.Add(("scope", (int)filter.Scope.Value)); }
        if (filter?.IncludeExpired != true) conditions.Add($"({prefix}expires_at IS NULL OR {prefix}expires_at > CURRENT_TIMESTAMP)");
        if (filter?.Metadata is not null) conditions.Add(BuildMetadataFilter(filter.Metadata, prefix, startIndex, parameters));
        return (conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions), parameters);
    }

    private static string BuildMetadataFilter(FilterExpression expression, string prefix, int startIndex, List<(string Name, object Value)> parameters) => expression switch
    {
        MetadataFilter condition => BuildMetadataCondition(condition, prefix, startIndex, parameters),
        FilterGroup { Logic: FilterLogic.And } group => BuildGroup("AND", group.Filters, prefix, startIndex, parameters),
        FilterGroup { Logic: FilterLogic.Or } group => BuildGroup("OR", group.Filters, prefix, startIndex, parameters),
        FilterGroup { Logic: FilterLogic.Not } group when group.Filters.Count == 1 => $"NOT ({BuildMetadataFilter(group.Filters[0], prefix, startIndex, parameters)})",
        FilterGroup { Logic: FilterLogic.Not } => throw new ArgumentException("A Not filter group must contain exactly one expression."),
        _ => throw new ArgumentOutOfRangeException(nameof(expression))
    };

    private static string BuildGroup(string operation, IReadOnlyList<FilterExpression> filters, string prefix, int startIndex, List<(string Name, object Value)> parameters)
    {
        if (filters.Count == 0) return operation == "AND" ? "TRUE" : "FALSE";
        return "(" + string.Join($" {operation} ", filters.Select(filter => BuildMetadataFilter(filter, prefix, startIndex, parameters))) + ")";
    }

    private static string BuildMetadataCondition(MetadataFilter condition, string prefix, int startIndex, List<(string Name, object Value)> parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(condition.Key);
        var keyParameter = AddParameter("metadata_key", condition.Key, startIndex, parameters);
        var valueExpression = $"{prefix}metadata ->> ${keyParameter}";
        if (condition.Operator == FilterOperator.Exists)
        {
            var exists = Convert.ToBoolean(condition.Value ?? true, CultureInfo.InvariantCulture);
            return exists ? $"{prefix}metadata ? ${keyParameter}" : $"NOT ({prefix}metadata ? ${keyParameter})";
        }

        if (condition.Operator is FilterOperator.In or FilterOperator.NotIn)
        {
            var values = FilterValues(condition.Value).ToArray();
            if (values.Length == 0) return condition.Operator == FilterOperator.In ? "FALSE" : "TRUE";
            var valueParameters = values.Select(value => $"${AddParameter("metadata_value", value, startIndex, parameters)}");
            return $"{valueExpression} {(condition.Operator == FilterOperator.In ? "IN" : "NOT IN")} ({string.Join(", ", valueParameters)})";
        }

        var expected = Convert.ToString(condition.Value, CultureInfo.InvariantCulture) ?? string.Empty;
        var expectedParameter = AddParameter("metadata_value", expected, startIndex, parameters);
        return condition.Operator switch
        {
            FilterOperator.Equal => $"{valueExpression} = ${expectedParameter}",
            FilterOperator.NotEqual => $"{valueExpression} <> ${expectedParameter}",
            FilterOperator.GreaterThan => BuildComparison(valueExpression, ">", expectedParameter),
            FilterOperator.GreaterThanOrEqual => BuildComparison(valueExpression, ">=", expectedParameter),
            FilterOperator.LessThan => BuildComparison(valueExpression, "<", expectedParameter),
            FilterOperator.LessThanOrEqual => BuildComparison(valueExpression, "<=", expectedParameter),
            FilterOperator.Contains => $"POSITION(${expectedParameter} IN {valueExpression}) > 0",
            FilterOperator.ContainsIgnoreCase => $"POSITION(LOWER(${expectedParameter}) IN LOWER({valueExpression})) > 0",
            _ => throw new ArgumentOutOfRangeException(nameof(condition.Operator))
        };
    }

    private static string BuildComparison(string actual, string operation, int expectedParameter) =>
        $"CASE WHEN {actual} ~ '^-?[0-9]+([.][0-9]+)?$' AND ${expectedParameter} ~ '^-?[0-9]+([.][0-9]+)?$' THEN ({actual})::numeric {operation} (${expectedParameter})::numeric ELSE {actual} {operation} ${expectedParameter} END";

    private static int AddParameter(string name, object value, int startIndex, List<(string Name, object Value)> parameters)
    {
        parameters.Add((name, value));
        return startIndex + parameters.Count - 1;
    }

    private static IEnumerable<object> FilterValues(object? value)
    {
        if (value is null) return [string.Empty];
        if (value is string text) return [text];
        return value is System.Collections.IEnumerable values
            ? values.Cast<object>().Select(item => Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty)
            : [Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty];
    }

    private static void AddFilter(string? value, string column, string name, List<string> conditions, List<(string, object)> parameters, int startIndex)
    {
        if (value is null) return;
        conditions.Add($"{column} = ${startIndex + parameters.Count}"); parameters.Add((name, value));
    }

    private static void AddParameters(NpgsqlCommand command, List<(string Name, object Value)> parameters)
    {
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Value);
    }

    private static string ToVectorLiteral(IReadOnlyList<float> embedding) => $"[{string.Join(',', embedding.Select(value => value.ToString("R", CultureInfo.InvariantCulture)))}]";
}