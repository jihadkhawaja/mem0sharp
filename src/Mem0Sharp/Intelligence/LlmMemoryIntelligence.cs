using System.Text.Json;

namespace Mem0Sharp;

public sealed class LlmMemoryConflictResolver : IMemoryConflictResolver
{
    private readonly IChatCompletionClient client;

    public LlmMemoryConflictResolver(IChatCompletionClient client) => this.client = client;

    public async Task<IReadOnlyList<MemoryDecision>> ResolveAsync(IReadOnlyList<Message> messages, IReadOnlyList<Memory> existingMemories, MemoryAddOptions options, CancellationToken cancellationToken = default)
    {
        var existing = existingMemories.Select((memory, index) => new { id = index.ToString(), text = memory.Text }).ToArray();
        var response = await client.CompleteAsync(
        [
            new Message("system", "Extract durable facts and reconcile them with existing memories. Return JSON only: {\"memory\":[{\"text\":\"fact\",\"event\":\"ADD|UPDATE|DELETE|NONE\",\"id\":\"existing numeric id when required\"}]}"),
            new Message("user", JsonSerializer.Serialize(new { existing, messages, instructions = options.Prompt }))
        ], cancellationToken);

        using var document = JsonDocument.Parse(StripCodeFence(response));
        if (!document.RootElement.TryGetProperty("memory", out var items) || items.ValueKind != JsonValueKind.Array) return [];
        var decisions = new List<MemoryDecision>();
        foreach (var item in items.EnumerateArray())
        {
            var text = item.TryGetProperty("text", out var textValue) ? textValue.GetString() ?? string.Empty : string.Empty;
            var eventName = item.TryGetProperty("event", out var eventValue) ? eventValue.GetString() : null;
            if (!Enum.TryParse<MemoryAction>(eventName, true, out var eventType)) continue;
            string? memoryId = null;
            if (item.TryGetProperty("id", out var idValue) && int.TryParse(idValue.GetString(), out var index) && index >= 0 && index < existingMemories.Count)
            {
                memoryId = existingMemories[index].Id;
            }
            if (eventType is MemoryAction.Update or MemoryAction.Delete && memoryId is null) continue;
            decisions.Add(new MemoryDecision(text.Trim(), eventType, memoryId));
        }
        return decisions;
    }

    private static string StripCodeFence(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;
        var firstNewLine = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewLine >= 0 && lastFence > firstNewLine ? trimmed[(firstNewLine + 1)..lastFence].Trim() : trimmed;
    }
}

public sealed class LlmProceduralMemoryGenerator : IProceduralMemoryGenerator
{
    private readonly IChatCompletionClient client;

    public LlmProceduralMemoryGenerator(IChatCompletionClient client) => this.client = client;

    public Task<string> GenerateAsync(IReadOnlyList<Message> messages, string? prompt = null, CancellationToken cancellationToken = default) =>
        client.CompleteAsync(
        [
            new Message("system", prompt ?? "Summarize the agent procedure as concise, reusable steps. Preserve tool names, ordering, decisions, and failure recovery."),
            .. messages
        ], cancellationToken);
}