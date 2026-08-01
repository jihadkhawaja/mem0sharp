using Mem0Sharp;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Mem0Sharp.Tests;

public sealed class PostgresHistoryIntegrationTests
{
    [Fact]
    public async Task HistoryPersistsAndMigratesOnPostgres()
    {
        await using var container = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
        await container.StartAsync();
        await CreateLegacyHistoryTableAsync(container.GetConnectionString());

        await using var store = new PostgresMemoryStore(new PostgresMemoryStoreOptions
        {
            ConnectionString = container.GetConnectionString(),
            EmbeddingDimensions = 8,
            UseHnswIndex = false
        });
        await store.InitializeAsync();

        var legacy = Assert.Single(await store.GetHistoryAsync("legacy-memory"));
        Assert.Equal(legacy.CreatedAt, legacy.UpdatedAt);
        Assert.False(legacy.IsDeleted);

        var service = new MemoryService(store, new LocalEmbeddingGenerator(8));
        var added = await service.AddAsync("old preference", new MemoryAddOptions
        {
            UserId = "alice",
            Metadata = new Dictionary<string, string> { ["actor_id"] = "assistant", ["role"] = "writer" }
        });
        var id = added.Memories[0].Id;
        await service.UpdateAsync(id, "new preference");
        await service.DeleteAsync(id);

        var history = await new MemoryService(store, new LocalEmbeddingGenerator(8)).GetHistoryAsync(id);

        Assert.Equal([MemoryHistoryEvent.Add, MemoryHistoryEvent.Update, MemoryHistoryEvent.Delete], history.Select(entry => entry.Event));
        Assert.All(history, entry => Assert.Equal(history[0].CreatedAt, entry.CreatedAt));
        Assert.InRange(Math.Abs((history[0].CreatedAt - added.Memories[0].CreatedAt).Ticks), 0, 9);
        Assert.Equal("assistant", history[0].ActorId);
        Assert.Equal("writer", history[0].Role);
        Assert.False(history[0].IsDeleted);
        Assert.False(history[1].IsDeleted);
        Assert.True(history[2].IsDeleted);
        Assert.True(history[0].UpdatedAt <= history[1].UpdatedAt);
        Assert.True(history[1].UpdatedAt <= history[2].UpdatedAt);

        await service.ResetAsync();
        Assert.Empty(await service.GetHistoryAsync(id));
    }

    private static async Task CreateLegacyHistoryTableAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            CREATE TABLE mem0_memories_history (
                id text PRIMARY KEY,
                memory_id text NOT NULL,
                event integer NOT NULL,
                old_memory text NULL,
                new_memory text NULL,
                created_at timestamptz NOT NULL
            );
            INSERT INTO mem0_memories_history (id, memory_id, event, old_memory, new_memory, created_at)
            VALUES ('legacy-event', 'legacy-memory', 0, NULL, 'legacy', '2025-01-01T00:00:00Z');
            """, connection);
        await command.ExecuteNonQueryAsync();
    }
}