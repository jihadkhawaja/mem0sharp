using System.Text.Json.Nodes;
using Mem0Sharp;
using Xunit;

namespace Mem0Sharp.Tests;

public sealed class MemoryMcpServerTests
{
    [Fact]
    public async Task ListsAllLocalMemoryTools()
    {
        var server = new MemoryMcpServer(new MemoryService());

        var response = await server.HandleAsync(JsonNode.Parse("""
            {"jsonrpc":"2.0","id":1,"method":"tools/list"}
            """)!);

        var tools = response!["result"]!["tools"]!.AsArray();
        Assert.Equal(9, tools.Count);
        Assert.Contains(tools, tool => tool!["name"]!.GetValue<string>() == "add_memory");
        Assert.Contains(tools, tool => tool!["name"]!.GetValue<string>() == "delete_entities");
    }

    [Fact]
    public async Task AddMemoryToolWritesToTheLocalService()
    {
        var memory = new MemoryService();
        var server = new MemoryMcpServer(memory);

        var response = await server.HandleAsync(JsonNode.Parse("""
            {
              "jsonrpc":"2.0",
              "id":"add-1",
              "method":"tools/call",
              "params":{"name":"add_memory","arguments":{"text":"local only","user_id":"alice","infer":false}}
            }
            """)!);

        Assert.False(response!["result"]!["isError"]!.GetValue<bool>());
        var stored = Assert.Single(await memory.GetAllAsync(new MemoryFilter(UserId: "alice")));
        Assert.Equal("local only", stored.Text);
    }
}