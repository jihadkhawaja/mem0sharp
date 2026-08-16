using Mem0Sharp;
using Xunit;

namespace Mem0Sharp.Tests;

public sealed class StateRollbackTests
{
    [Fact]
    public async Task InMemoryStoreRollbackRestoresStateAtTimestamp()
    {
        var service = new MemoryService();

        var t0 = DateTimeOffset.UtcNow;
        await Task.Delay(10);

        var add1 = await service.AddAsync("Alice loves pizza", "alice");
        var id1 = add1.Memories[0].Id;

        await Task.Delay(20);
        var t1 = DateTimeOffset.UtcNow;
        await Task.Delay(20);

        await service.UpdateAsync(id1, "Alice loves sushi");

        var add2 = await service.AddAsync("Alice drives a Tesla", "alice");
        var id2 = add2.Memories[0].Id;

        await Task.Delay(20);
        var t2 = DateTimeOffset.UtcNow;

        // Current state
        var currentMemories = await service.GetAllAsync(new MemoryFilter(UserId: "alice"));
        Assert.Equal(2, currentMemories.Count);
        Assert.Equal("Alice loves sushi", currentMemories.First(m => m.Id == id1).Text);

        // Roll back to t1 (before the update and before id2 was added)
        var rollbackResult = await service.RollbackAsync(t1);

        var rolledBackMemories = await service.GetAllAsync(new MemoryFilter(UserId: "alice"));
        Assert.Single(rolledBackMemories);
        Assert.Equal("Alice loves pizza", rolledBackMemories[0].Text);
        Assert.Equal(id1, rolledBackMemories[0].Id);
        Assert.True(rollbackResult.RestoredCount > 0 || rollbackResult.DeletedCount > 0);
    }

    [Fact]
    public async Task SqliteStoreRollbackRestoresPreviousMemoryState()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"mem0_rollback_test_{Guid.NewGuid():N}.db");
        try
        {
            await using var store = new SqliteMemoryStore(dbPath);
            await store.InitializeAsync();
            var service = new MemoryService(store);

            var add1 = await service.AddAsync("Original fact", "bob");
            var id1 = add1.Memories[0].Id;

            await Task.Delay(50);
            var t1 = DateTimeOffset.UtcNow;
            await Task.Delay(50);

            await service.UpdateAsync(id1, "Poisoned or modified fact");
            await service.AddAsync("Spurious memory", "bob");

            // Verify corrupted state
            var current = await service.GetAllAsync(new MemoryFilter(UserId: "bob"));
            Assert.Equal(2, current.Count);
            Assert.Equal("Poisoned or modified fact", current.First(m => m.Id == id1).Text);

            // Rollback to t1
            var result = await service.RollbackAsync(t1);

            var restored = await service.GetAllAsync(new MemoryFilter(UserId: "bob"));
            Assert.Single(restored);
            Assert.Equal("Original fact", restored[0].Text);
            Assert.Equal(id1, restored[0].Id);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
