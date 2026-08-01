using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mem0Sharp;

public sealed class MemoryMcpServer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IMemoryService memory;

    public MemoryMcpServer(IMemoryService memory) => this.memory = memory;

    public async Task<JsonObject?> HandleAsync(JsonNode request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var method = request["method"]?.GetValue<string>();
        var id = request["id"]?.DeepClone();
        if (method is null) return Error(id, -32600, "Invalid JSON-RPC request.");

        try
        {
            return method switch
            {
                "notifications/initialized" => null,
                "initialize" => Response(id, new JsonObject
                {
                    ["protocolVersion"] = request["params"]?["protocolVersion"]?.GetValue<string>() ?? "2025-06-18",
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
                    ["serverInfo"] = new JsonObject { ["name"] = "mem0sharp", ["version"] = "0.1.0" }
                }),
                "tools/list" => Response(id, new JsonObject { ["tools"] = CreateTools() }),
                "tools/call" => Response(id, await CallToolAsync(request["params"]?.AsObject(), cancellationToken)),
                _ => Error(id, -32601, $"Method '{method}' was not found.")
            };
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException or InvalidOperationException)
        {
            return Response(id, ToolResult(new { error = exception.Message }, true));
        }
    }

    public async Task RunAsync(Stream input, Stream output, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(input, leaveOpen: true);
        await using var writer = new StreamWriter(output, leaveOpen: true) { AutoFlush = true };
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonNode? request;
            try
            {
                request = JsonNode.Parse(line);
            }
            catch (JsonException)
            {
                await writer.WriteLineAsync(Error(null, -32700, "Parse error.").ToJsonString(JsonOptions));
                continue;
            }
            if (request is null) continue;
            var response = await HandleAsync(request, cancellationToken);
            if (response is not null) await writer.WriteLineAsync(response.ToJsonString(JsonOptions));
        }
    }

    private async Task<JsonObject> CallToolAsync(JsonObject? parameters, CancellationToken cancellationToken)
    {
        var name = parameters?["name"]?.GetValue<string>() ?? throw new ArgumentException("Tool name is required.");
        var arguments = parameters?["arguments"] as JsonObject ?? new JsonObject();
        object result = name switch
        {
            "add_memory" => await AddMemoryAsync(arguments, cancellationToken),
            "search_memories" => await SearchMemoriesAsync(arguments, cancellationToken),
            "get_memories" => await memory.GetAllAsync(Filter(arguments, includeExpired: Bool(arguments, "include_expired")), cancellationToken),
            "get_memory" => await memory.GetAsync(Required(arguments, "memory_id"), cancellationToken) ?? throw new KeyNotFoundException("Memory was not found."),
            "update_memory" => await memory.UpdateAsync(Required(arguments, "memory_id"), new MemoryUpdate { Text = Required(arguments, "text") }, cancellationToken),
            "delete_memory" => await DeleteMemoryAsync(arguments, cancellationToken),
            "delete_all_memories" => await memory.DeleteAllAsync(Filter(arguments, includeExpired: true), cancellationToken),
            "list_entities" => await ListEntitiesAsync(cancellationToken),
            "delete_entities" => await DeleteEntitiesAsync(arguments, cancellationToken),
            _ => throw new ArgumentException($"Unknown tool '{name}'.")
        };
        return ToolResult(result);
    }

    private async Task<object> AddMemoryAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var result = await memory.AddAsync(Required(arguments, "text"), new MemoryAddOptions
        {
            UserId = Text(arguments, "user_id") ?? "default_user",
            AgentId = Text(arguments, "agent_id"),
            RunId = Text(arguments, "run_id"),
            Infer = Bool(arguments, "infer", true),
            Behavior = Behavior(arguments),
            Prompt = Text(arguments, "prompt")
        }, cancellationToken);
        return result;
    }

    private Task<IReadOnlyList<SearchResult>> SearchMemoriesAsync(JsonObject arguments, CancellationToken cancellationToken) =>
        memory.SearchAsync(Required(arguments, "query"), new MemorySearchOptions
        {
            Filter = Filter(arguments, includeExpired: Bool(arguments, "include_expired")),
            TopK = Integer(arguments, "top_k", 10),
            Threshold = Number(arguments, "threshold", 0.1),
            Rerank = Bool(arguments, "rerank"),
            Explain = Bool(arguments, "explain")
        }, cancellationToken);

    private async Task<object> DeleteMemoryAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        await memory.DeleteAsync(Required(arguments, "memory_id"), cancellationToken);
        return new { deleted = true };
    }

    private async Task<IReadOnlyList<object>> ListEntitiesAsync(CancellationToken cancellationToken)
    {
        var memories = await memory.GetAllAsync(new MemoryFilter(IncludeExpired: true), cancellationToken);
        return memories.Select(memory => (Type: "user", Name: (string?)memory.UserId))
            .Concat(memories.Select(memory => (Type: "agent", Name: memory.AgentId)))
            .Concat(memories.Select(memory => (Type: "run", Name: memory.RunId)))
            .Where(entity => !string.IsNullOrWhiteSpace(entity.Name))
            .Distinct()
            .Select(entity => (object)new { type = entity.Type, name = entity.Name })
            .ToArray();
    }

    private async Task<object> DeleteEntitiesAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var type = Required(arguments, "entity_type").ToLowerInvariant();
        var name = Required(arguments, "entity_name");
        var filter = type switch
        {
            "user" => new MemoryFilter(UserId: name, IncludeExpired: true),
            "agent" => new MemoryFilter(AgentId: name, IncludeExpired: true),
            "run" => new MemoryFilter(RunId: name, IncludeExpired: true),
            _ => throw new ArgumentException("entity_type must be user, agent, or run.")
        };
        return new { deleted = await memory.DeleteAllAsync(filter, cancellationToken) };
    }

    private static MemoryFilter Filter(JsonObject arguments, bool includeExpired = false) => new(
        Text(arguments, "user_id"), Text(arguments, "agent_id"), Text(arguments, "run_id"), IncludeExpired: includeExpired);

    private static JsonArray CreateTools() =>
    [
        Tool("add_memory", "Store a memory in the local C# memory service.", Properties(("text", "string"), ("user_id", "string"), ("agent_id", "string"), ("run_id", "string"), ("infer", "boolean"), ("behavior", "string"), ("prompt", "string")), ["text"]),
        Tool("search_memories", "Search local memories.", Properties(("query", "string"), ("user_id", "string"), ("agent_id", "string"), ("run_id", "string"), ("top_k", "integer"), ("threshold", "number"), ("rerank", "boolean"), ("explain", "boolean")), ["query"]),
        Tool("get_memories", "List local memories.", Properties(("user_id", "string"), ("agent_id", "string"), ("run_id", "string"), ("include_expired", "boolean"))),
        Tool("get_memory", "Get one local memory.", Properties(("memory_id", "string")), ["memory_id"]),
        Tool("update_memory", "Update one local memory.", Properties(("memory_id", "string"), ("text", "string")), ["memory_id", "text"]),
        Tool("delete_memory", "Delete one local memory.", Properties(("memory_id", "string")), ["memory_id"]),
        Tool("delete_all_memories", "Delete matching local memories.", Properties(("user_id", "string"), ("agent_id", "string"), ("run_id", "string"))),
        Tool("list_entities", "List user, agent, and run identifiers in local memory.", new JsonObject()),
        Tool("delete_entities", "Delete memories associated with an entity.", Properties(("entity_type", "string"), ("entity_name", "string")), ["entity_type", "entity_name"])
    ];

    private static JsonObject Tool(string name, string description, JsonObject properties, string[]? required = null) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required is null ? new JsonArray() : new JsonArray(required.Select(item => (JsonNode?)JsonValue.Create(item)).ToArray())
        }
    };

    private static JsonObject Properties(params (string Name, string Type)[] fields)
    {
        var properties = new JsonObject();
        foreach (var field in fields) properties[field.Name] = new JsonObject { ["type"] = field.Type };
        return properties;
    }

    private static JsonObject ToolResult(object value, bool isError = false) => new()
    {
        ["content"] = new JsonArray(new JsonObject
        {
            ["type"] = "text",
            ["text"] = JsonSerializer.Serialize(value, JsonOptions)
        }),
        ["isError"] = isError
    };

    private static JsonObject Response(JsonNode? id, JsonNode result) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["result"] = result
    };

    private static JsonObject Error(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
    };

    private static string Required(JsonObject arguments, string name) => Text(arguments, name) ?? throw new ArgumentException($"'{name}' is required.");
    private static string? Text(JsonObject arguments, string name) => arguments[name]?.GetValue<string>();
    private static MemoryBehavior Behavior(JsonObject arguments)
    {
        var value = Text(arguments, "behavior");
        if (string.IsNullOrWhiteSpace(value)) return MemoryBehavior.Normal;
        var normalized = value.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        return Enum.TryParse<MemoryBehavior>(normalized, true, out var behavior) && Enum.IsDefined(behavior)
            ? behavior
            : throw new ArgumentException("'behavior' must be normal, dreaming, random_thoughts, or personal_memory.");
    }
    private static bool Bool(JsonObject arguments, string name, bool fallback = false) => arguments[name]?.GetValue<bool>() ?? fallback;
    private static int Integer(JsonObject arguments, string name, int fallback) => arguments[name]?.GetValue<int>() ?? fallback;
    private static double Number(JsonObject arguments, string name, double fallback) => arguments[name]?.GetValue<double>() ?? fallback;
}
