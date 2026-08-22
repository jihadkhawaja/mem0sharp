using Mem0Sharp;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Mem0Sharp.Tests;

public sealed class SqliteMemoryStoreTests
{
    [Fact]
    public async Task InitializeCreatesTheNormalizedMemoryAndHistoryColumns()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"mem0sharp-{Guid.NewGuid():N}.db");
        try
        {
            await using var store = new SqliteMemoryStore(databasePath);
            await store.InitializeAsync();
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
            await connection.OpenAsync();

            Assert.Equal(
                ["id", "text_value", "user_id", "agent_id", "run_id", "scope", "metadata", "embedding", "created_at", "updated_at", "expires_at", "hash_value", "behavior", "memory_type"],
                await GetColumnsAsync(connection, "memories"));
            Assert.Equal(
                ["id", "memory_id", "event", "old_memory", "new_memory", "created_at", "updated_at", "is_deleted", "actor_id", "role"],
                await GetColumnsAsync(connection, "memory_history"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task InitializeMigratesNormalizedTablesCreatedBeforeProvenance()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"mem0sharp-{Guid.NewGuid():N}.db");
        try
        {
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString()))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE memories (
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
                    """;
                await command.ExecuteNonQueryAsync();
            }

            await using var store = new SqliteMemoryStore(databasePath);
            await store.InitializeAsync();
            var memory = Memory("legacy", "alice") with { Behavior = MemoryBehavior.PersonalMemory, MemoryType = "persona" };
            await store.SaveAsync(memory, [1, 0]);

            var loaded = await store.GetAsync(memory.Id);
            Assert.NotNull(loaded);
            Assert.Equal(MemoryBehavior.PersonalMemory, loaded.Behavior);
            Assert.Equal("persona", loaded.MemoryType);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task VectorSearchPersistsEmbeddingsAndRanksByCosineSimilarity()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"mem0sharp-{Guid.NewGuid():N}.db");
        try
        {
            var firstStore = new SqliteMemoryStore(databasePath);
            await firstStore.InitializeAsync();
            await firstStore.SaveBatchAsync(
            [
                new MemoryVectorRecord(Memory("first", "alice"), [1, 0]),
                new MemoryVectorRecord(Memory("second", "alice"), [0.8f, 0.2f]),
                new MemoryVectorRecord(Memory("other user", "bob"), [1, 0])
            ]);
            await firstStore.DisposeAsync();

            var reopenedStore = new SqliteMemoryStore(databasePath);
            await reopenedStore.InitializeAsync();
            var results = await reopenedStore.SearchAsync([1, 0], new MemoryFilter(UserId: "alice"), topK: 2);

            Assert.Equal(["first", "second"], results.Select(result => result.Memory.Text));
            Assert.Equal(1, results[0].Score, 5);
            await reopenedStore.DisposeAsync();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task VectorSearchRejectsMismatchedDimensions()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"mem0sharp-{Guid.NewGuid():N}.db");
        try
        {
            await using var store = new SqliteMemoryStore(databasePath);
            await store.InitializeAsync();
            await store.SaveAsync(Memory("memory", "alice"), [1, 0]);

            await Assert.ThrowsAsync<InvalidDataException>(() => store.SearchAsync([1, 0, 0]));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task ServicePersistsMetadataAndHistoryAcrossReopen()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"mem0sharp-{Guid.NewGuid():N}.db");
        try
        {
            var firstStore = new SqliteMemoryStore(databasePath);
            await firstStore.InitializeAsync();
            var firstService = new MemoryService(firstStore);
            var added = await firstService.AddAsync("old preference", new MemoryAddOptions
            {
                UserId = "alice",
                Infer = false,
                Behavior = MemoryBehavior.Dreaming,
                MemoryType = "association",
                Metadata = new Dictionary<string, string> { ["source"] = "test" }
            });
            var id = added.Memories[0].Id;
            await firstService.UpdateAsync(id, "new preference");
            await firstStore.DisposeAsync();

            await using var reopenedStore = new SqliteMemoryStore(databasePath);
            await reopenedStore.InitializeAsync();
            var reopenedService = new MemoryService(reopenedStore);
            var memory = Assert.Single(await reopenedService.GetAllAsync());
            var history = await reopenedService.GetHistoryAsync(id);

            Assert.Equal("new preference", memory.Text);
            Assert.Equal(MemoryBehavior.Dreaming, memory.Behavior);
            Assert.Equal("association", memory.MemoryType);
            Assert.Equal("test", memory.Metadata["source"]);
            Assert.Collection(
                history,
                entry => Assert.Equal(MemoryHistoryEvent.Add, entry.Event),
                entry =>
                {
                    Assert.Equal(MemoryHistoryEvent.Update, entry.Event);
                    Assert.Equal("old preference", entry.OldMemory);
                    Assert.Equal("new preference", entry.NewMemory);
                });
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task BulkDeleteRemovesOnlyMatchingMemoriesAndEmbeddings()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"mem0sharp-{Guid.NewGuid():N}.db");
        try
        {
            await using var store = new SqliteMemoryStore(databasePath);
            await store.InitializeAsync();
            var alice = Memory("alice fact", "alice");
            var bob = Memory("bob fact", "bob");
            await store.SaveBatchAsync([
                new MemoryVectorRecord(alice, [1, 0]),
                new MemoryVectorRecord(bob, [0, 1])
            ]);

            Assert.Equal(1, await store.DeleteAllAsync(new MemoryFilter(UserId: "alice")));

            Assert.Null(await store.GetAsync(alice.Id));
            Assert.NotNull(await store.GetAsync(bob.Id));
            Assert.Empty(await store.SearchAsync([1, 0], new MemoryFilter(UserId: "alice")));
            Assert.Single(await store.SearchAsync([0, 1], new MemoryFilter(UserId: "bob")));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task ResetClearsMemoriesAndHistory()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"mem0sharp-{Guid.NewGuid():N}.db");
        try
        {
            await using var store = new SqliteMemoryStore(databasePath);
            await store.InitializeAsync();
            var memory = Memory("remember me", "alice");
            await store.SaveAsync(memory, [1, 0]);
            await store.SaveHistoryAsync(new MemoryHistoryEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                MemoryId = memory.Id,
                Event = MemoryHistoryEvent.Add,
                NewMemory = memory.Text,
                CreatedAt = memory.CreatedAt,
                UpdatedAt = memory.UpdatedAt
            });

            await store.ResetAsync();

            Assert.Empty(await store.GetHistoryAsync(memory.Id));
            Assert.Empty(await MaterializeAsync(store.GetAllAsync()));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task MixedDimensionBatchDoesNotPersistPartialRecords()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"mem0sharp-{Guid.NewGuid():N}.db");
        try
        {
            await using var store = new SqliteMemoryStore(databasePath);
            await store.InitializeAsync();

            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveBatchAsync([
                new MemoryVectorRecord(Memory("first", "alice"), [1, 0]),
                new MemoryVectorRecord(Memory("second", "alice"), [1, 0, 0])
            ]));

            Assert.Empty(await MaterializeAsync(store.GetAllAsync()));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task AtomicBatchRollsBackMemoriesWhenHistoryWriteFails()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"mem0sharp-{Guid.NewGuid():N}.db");
        try
        {
            await using var store = new SqliteMemoryStore(databasePath);
            await store.InitializeAsync();
            var first = Memory("first", "alice");
            var second = Memory("second", "alice");
            var historyId = Guid.NewGuid().ToString("N");

            await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => store.SaveBatchAsync([
                new MemoryWriteRecord(first, [1, 0], History(historyId, first)),
                new MemoryWriteRecord(second, [0, 1], History(historyId, second))
            ]));

            Assert.Empty(await MaterializeAsync(store.GetAllAsync()));
            Assert.Empty(await store.GetHistoryAsync(first.Id));
            Assert.Empty(await store.GetHistoryAsync(second.Id));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    private static async Task<IReadOnlyList<T>> MaterializeAsync<T>(IAsyncEnumerable<T> source)
    {
        var items = new List<T>();
        await foreach (var item in source) items.Add(item);
        return items;
    }

    private static Memory Memory(string text, string userId) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Text = text,
        UserId = userId,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        Hash = text
    };

    private static MemoryHistoryEntry History(string id, Memory memory) => new()
    {
        Id = id,
        MemoryId = memory.Id,
        Event = MemoryHistoryEvent.Add,
        NewMemory = memory.Text,
        CreatedAt = memory.CreatedAt,
        UpdatedAt = memory.UpdatedAt
    };

    private static async Task<IReadOnlyList<string>> GetColumnsAsync(SqliteConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName})";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
        return columns;
    }
}
