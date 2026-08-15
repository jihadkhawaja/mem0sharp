using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;

namespace Mem0Sharp;

public sealed class PostgresEntityStore : IEntityStore
{
    private readonly string connectionString;
    private readonly string tableName;

    public PostgresEntityStore(PostgresMemoryStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        connectionString = options.ConnectionString;
        tableName = PostgresIdentifier.Table(options.TableName + "_entities");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"""
            CREATE TABLE IF NOT EXISTS {tableName} (
                id text PRIMARY KEY,
                normalized_text text NOT NULL UNIQUE,
                text_value text NOT NULL,
                entity_type integer NOT NULL,
                linked_memory_ids jsonb NOT NULL
            );
            """, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertLinksAsync(IReadOnlyList<ExtractedEntity> entities, string memoryId, CancellationToken cancellationToken = default)
    {
        if (entities.Count == 0) return;
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var entity in entities.GroupBy(item => Normalize(item.Text), StringComparer.Ordinal).Select(group => group.First()))
        {
            await using var command = new NpgsqlCommand($"""
                INSERT INTO {tableName} (id, normalized_text, text_value, entity_type, linked_memory_ids)
                VALUES ($1, $2, $3, $4, jsonb_build_array($5::text))
                ON CONFLICT (normalized_text) DO UPDATE SET
                    text_value = EXCLUDED.text_value,
                    entity_type = EXCLUDED.entity_type,
                    linked_memory_ids = CASE
                        WHEN {tableName}.linked_memory_ids ? $5 THEN {tableName}.linked_memory_ids
                        ELSE {tableName}.linked_memory_ids || jsonb_build_array($5::text)
                    END
                """, connection, transaction);
            command.Parameters.AddWithValue(Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue(Normalize(entity.Text));
            command.Parameters.AddWithValue(entity.Text.Trim());
            command.Parameters.AddWithValue((int)entity.Type);
            command.Parameters.AddWithValue(memoryId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RemoveMemoryAsync(string memoryId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"""
            UPDATE {tableName} SET linked_memory_ids = linked_memory_ids - $1 WHERE linked_memory_ids ? $1;
            DELETE FROM {tableName} WHERE jsonb_array_length(linked_memory_ids) = 0;
            """, connection);
        command.Parameters.AddWithValue(memoryId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, double>> GetMemoryBoostsAsync(IReadOnlyList<ExtractedEntity> entities, CancellationToken cancellationToken = default)
    {
        var normalized = entities.Select(entity => Normalize(entity.Text)).Distinct(StringComparer.Ordinal).ToArray();
        if (normalized.Length == 0) return new Dictionary<string, double>();
        await using var connection = await OpenAsync(cancellationToken);
        var placeholders = normalized.Select((_, index) => $"${index + 1}").ToArray();
        await using var command = new NpgsqlCommand($"SELECT linked_memory_ids FROM {tableName} WHERE normalized_text IN ({string.Join(", ", placeholders)})", connection);
        foreach (var value in normalized) command.Parameters.AddWithValue(value);
        var boosts = new Dictionary<string, double>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var links = JsonSerializer.Deserialize<string[]>(reader.GetString(0)) ?? [];
            var contribution = 0.5 / Math.Max(links.Length, 1);
            foreach (var memoryId in links) boosts[memoryId] = Math.Min(0.5, boosts.GetValueOrDefault(memoryId) + contribution);
        }
        return boosts;
    }

    public async Task<IReadOnlyList<MemoryEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"SELECT id, text_value, entity_type, linked_memory_ids FROM {tableName} ORDER BY normalized_text", connection);
        var entities = new List<MemoryEntity>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var links = (JsonSerializer.Deserialize<string[]>(reader.GetString(3)) ?? []).ToHashSet(StringComparer.Ordinal);
            entities.Add(new MemoryEntity(reader.GetString(0), reader.GetString(1), (EntityType)reader.GetInt32(2), links));
        }
        return entities;
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"TRUNCATE TABLE {tableName}", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static string Normalize(string text) => string.Join(' ', text.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}

public sealed class PostgresGraphStore : IGraphMemoryStore
{
    private readonly string connectionString;
    private readonly string tableName;

    public PostgresGraphStore(PostgresMemoryStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        connectionString = options.ConnectionString;
        tableName = PostgresIdentifier.Table(options.TableName + "_relations");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"""
            CREATE TABLE IF NOT EXISTS {tableName} (
                id text PRIMARY KEY,
                source text NOT NULL,
                relationship text NOT NULL,
                target text NOT NULL,
                memory_id text NOT NULL
            );
            CREATE INDEX IF NOT EXISTS {PostgresIdentifier.Index(tableName, "memory_idx")} ON {tableName} (memory_id);
            """, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertAsync(IReadOnlyList<ExtractedRelation> relations, string memoryId, CancellationToken cancellationToken = default)
    {
        if (relations.Count == 0) return;
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var relation in relations.Where(IsValid).Distinct())
        {
            await using var command = new NpgsqlCommand($"INSERT INTO {tableName} (id, source, relationship, target, memory_id) VALUES ($1, $2, $3, $4, $5) ON CONFLICT (id) DO NOTHING", connection, transaction);
            command.Parameters.AddWithValue(RelationId(relation, memoryId));
            command.Parameters.AddWithValue(relation.Source.Trim());
            command.Parameters.AddWithValue(relation.Relationship.Trim());
            command.Parameters.AddWithValue(relation.Target.Trim());
            command.Parameters.AddWithValue(memoryId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RemoveMemoryAsync(string memoryId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"DELETE FROM {tableName} WHERE memory_id = $1", connection);
        command.Parameters.AddWithValue(memoryId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, double>> GetMemoryBoostsAsync(string query, CancellationToken cancellationToken = default)
    {
        var terms = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToArray();
        if (terms.Length == 0) return new Dictionary<string, double>();

        var patterns = terms.Select(term => $"%{term}%").ToArray();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"SELECT source, relationship, target, memory_id FROM {tableName} WHERE source ILIKE ANY($1) OR relationship ILIKE ANY($1) OR target ILIKE ANY($1)", connection);
        command.Parameters.AddWithValue(patterns);

        var boosts = new Dictionary<string, double>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var memoryId = reader.GetString(3);
            boosts[memoryId] = Math.Min(0.5, boosts.GetValueOrDefault(memoryId) + 0.25);
        }
        return boosts;
    }

    public async Task<IReadOnlyList<MemoryRelation>> GetRelationsAsync(string? query = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        NpgsqlCommand command;
        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = $"%{query.Trim()}%";
            command = new NpgsqlCommand($"SELECT id, source, relationship, target, memory_id FROM {tableName} WHERE source ILIKE $1 OR relationship ILIKE $1 OR target ILIKE $1 ORDER BY source, relationship, target", connection);
            command.Parameters.AddWithValue(pattern);
        }
        else
        {
            command = new NpgsqlCommand($"SELECT id, source, relationship, target, memory_id FROM {tableName} ORDER BY source, relationship, target", connection);
        }

        await using (command)
        {
            var relations = new List<MemoryRelation>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                relations.Add(new MemoryRelation(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
            }
            return relations;
        }
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"TRUNCATE TABLE {tableName}", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static string RelationId(ExtractedRelation relation, string memoryId)
    {
        var key = $"{Normalize(relation.Source)}|{Normalize(relation.Relationship)}|{Normalize(relation.Target)}|{memoryId}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
    }

    private static bool IsValid(ExtractedRelation relation) => !string.IsNullOrWhiteSpace(relation.Source) && !string.IsNullOrWhiteSpace(relation.Relationship) && !string.IsNullOrWhiteSpace(relation.Target);
    private static string Normalize(string text) => string.Join(' ', text.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}

internal static partial class PostgresIdentifier
{
    [GeneratedRegex("^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static string Table(string value)
    {
        if (!Pattern().IsMatch(value)) throw new ArgumentException("PostgreSQL object names must be simple identifiers.", nameof(value));
        return $"\"{value}\"";
    }

    public static string Index(string quotedTableName, string suffix)
    {
        var table = quotedTableName.Trim('"');
        return Table($"{table}_{suffix}");
    }
}
