using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;

namespace Mem0Sharp;

public sealed class PostgresMemoryStore : IVectorMemoryStore, IBulkMemoryStore, IAsyncDisposable
{
    private static readonly Regex IdentifierPattern = new("^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly PostgresMemoryStoreOptions options;
    private readonly string tableName;

    public PostgresMemoryStore(PostgresMemoryStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.EmbeddingDimensions < 1) throw new ArgumentOutOfRangeException(nameof(options.EmbeddingDimensions));
        if (!IdentifierPattern.IsMatch(options.TableName)) throw new ArgumentException("TableName must be a simple PostgreSQL identifier.", nameof(options));
        this.options = options;
        tableName = $"\"{options.TableName}\"";
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
                updated_at timestamptz NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "{options.TableName}_user_idx" ON {tableName} (user_id);
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

    public async Task<Memory?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"SELECT id, text_value, user_id, agent_id, run_id, scope, metadata, created_at, updated_at FROM {tableName} WHERE id = $1", connection);
        command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadMemory(reader) : null;
    }

    public async IAsyncEnumerable<Memory> GetAllAsync(MemoryFilter? filter = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var (where, parameters) = BuildFilter(filter);
        await using var command = new NpgsqlCommand($"SELECT id, text_value, user_id, agent_id, run_id, scope, metadata, created_at, updated_at FROM {tableName} {where} ORDER BY updated_at DESC", connection);
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
        await using var command = new NpgsqlCommand($"SELECT m.id, m.text_value, m.user_id, m.agent_id, m.run_id, m.scope, m.metadata, m.created_at, m.updated_at, 1 - (m.embedding <=> $1::vector) AS score FROM {tableName} m {embeddingCondition} ORDER BY m.embedding <=> $1::vector LIMIT ${topKParameter}", connection);
        command.Parameters.AddWithValue(ToVectorLiteral(embedding));
        AddParameters(command, parameters);
        command.Parameters.AddWithValue(topK);
        var results = new List<SearchResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) results.Add(new SearchResult(ReadMemory(reader), reader.GetDouble(9)));
        return results;
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

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task SaveCoreAsync(Memory memory, IReadOnlyList<float>? embedding, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"""
            INSERT INTO {tableName} (id, text_value, user_id, agent_id, run_id, scope, metadata, embedding, created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7::jsonb, $8::vector, $9, $10)
            ON CONFLICT (id) DO UPDATE SET text_value = EXCLUDED.text_value, user_id = EXCLUDED.user_id, agent_id = EXCLUDED.agent_id, run_id = EXCLUDED.run_id, scope = EXCLUDED.scope, metadata = EXCLUDED.metadata, embedding = COALESCE(EXCLUDED.embedding, {tableName}.embedding), updated_at = EXCLUDED.updated_at
            """, connection);
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
        Id = reader.GetString(0), Text = reader.GetString(1), UserId = reader.GetString(2), AgentId = reader.IsDBNull(3) ? null : reader.GetString(3), RunId = reader.IsDBNull(4) ? null : reader.GetString(4), Scope = (MemoryScope)reader.GetInt32(5), Metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(6)) ?? new(), CreatedAt = reader.GetFieldValue<DateTimeOffset>(7), UpdatedAt = reader.GetFieldValue<DateTimeOffset>(8)
    };

    private static (string Where, List<(string Name, object Value)> Parameters) BuildFilter(MemoryFilter? filter, string prefix = "", int startIndex = 1)
    {
        var conditions = new List<string>();
        var parameters = new List<(string, object)>();
        AddFilter(filter?.UserId, $"{prefix}user_id", "user_id", conditions, parameters, startIndex);
        AddFilter(filter?.AgentId, $"{prefix}agent_id", "agent_id", conditions, parameters, startIndex);
        AddFilter(filter?.RunId, $"{prefix}run_id", "run_id", conditions, parameters, startIndex);
        if (filter?.Scope is not null) { conditions.Add($"{prefix}scope = ${startIndex + parameters.Count}"); parameters.Add(("scope", (int)filter.Scope.Value)); }
        return (conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions), parameters);
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