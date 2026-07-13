using Mem0Sharp;
using Xunit;

namespace Mem0Sharp.Tests;

public sealed class MemoryServiceTests
{
    [Fact]
    public async Task AddAndSearchRanksRelatedMemory()
    {
        var service = new MemoryService();
        await service.AddAsync("I prefer dark mode and vim keybindings", "alice");
        await service.AddAsync("I enjoy hiking on weekends", "alice");

        var results = await service.SearchAsync("What editor settings does Alice prefer?", new MemoryFilter(UserId: "alice"));

        Assert.NotEmpty(results);
        Assert.Contains("dark mode", results[0].Memory.Text);
    }

    [Fact]
    public async Task FiltersDoNotLeakBetweenUsers()
    {
        var service = new MemoryService();
        await service.AddAsync("Alice likes tea", "alice");
        await service.AddAsync("Bob likes coffee", "bob");

        var searches = await service.SearchManyAsync(["tea", "coffee"], new MemoryFilter(UserId: "alice"));
        Assert.Equal(2, searches.Count);
        Assert.NotEmpty(searches[0]);
        Assert.Empty(searches[1]);

        var memories = await service.GetAllAsync(new MemoryFilter(UserId: "alice"));

        var memory = Assert.Single(memories);
        Assert.Equal("alice", memory.UserId);

        Assert.Equal(1, await service.DeleteAllAsync(new MemoryFilter(UserId: "alice")));
        Assert.Single(await service.GetAllAsync());
    }

    [Fact]
    public async Task UpdateAndDeleteChangeTheStore()
    {
        var service = new MemoryService();
        var added = await service.AddAsync("old preference", "alice");
        var id = added.Memories[0].Id;

        var updated = await service.UpdateAsync(id, "new preference");
        Assert.Equal("new preference", updated.Text);

        await service.DeleteAsync(id);
        Assert.Null(await service.GetAsync(id));
    }

}
