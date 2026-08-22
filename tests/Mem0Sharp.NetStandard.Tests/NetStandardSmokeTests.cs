using System.Runtime.Versioning;
using Mem0Sharp;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Mem0Sharp.NetStandard.Tests;

public sealed class NetStandardSmokeTests
{
    private const string NetStandardFrameworkName = ".NETStandard,Version=v2.0";

    [Fact]
    public void LoadsNetStandardAssetsForAllPackages()
    {
        Assert.Equal(NetStandardFrameworkName, FrameworkName(typeof(MemoryService)));
        Assert.Equal(NetStandardFrameworkName, FrameworkName(typeof(PostgresMemoryStore)));
        Assert.Equal(NetStandardFrameworkName, FrameworkName(typeof(SqliteMemoryStore)));
    }

    [Fact]
    public async Task CoreMemoryFlowRunsFromNetStandardAsset()
    {
        var service = new MemoryService();

        var added = await service.AddAsync("Alice prefers dark mode", "alice");
        var result = Assert.Single(await service.SearchAsync("dark mode", new MemoryFilter(UserId: "alice")));

        Assert.Equal(Assert.Single(added.Memories).Id, result.Memory.Id);
    }

    [Fact]
    public async Task SqliteFlowRunsFromNetStandardAsset()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"mem0sharp-netstandard-{Guid.NewGuid():N}.db");
        try
        {
            await using var store = new SqliteMemoryStore(databasePath);
            await store.InitializeAsync();
            var service = new MemoryService(store);

            await service.AddAsync("portable SQLite memory", "alice");

            Assert.Equal("portable SQLite memory", Assert.Single(await service.GetAllAsync()).Text);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    private static string? FrameworkName(Type type) => type.Assembly.GetCustomAttributes(typeof(TargetFrameworkAttribute), false)
        .Cast<TargetFrameworkAttribute>()
        .Single()
        .FrameworkName;
}