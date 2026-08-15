using System.Text.Json;

namespace Mem0Sharp;

public sealed class LlmMemoryExtractor : IMemoryExtractor
{
    private readonly IChatCompletionClient client;

    public LlmMemoryExtractor(IChatCompletionClient client) => this.client = client;

    public async Task<IReadOnlyList<MemoryInput>> ExtractAsync(IReadOnlyList<Message> messages, MemoryAddOptions? options = null, CancellationToken cancellationToken = default)
    {
        var instructions = options is null ? MemoryBehaviorPrompts.NormalExtraction : MemoryBehaviorPrompts.ForExtraction(options);
        var prompt = new Message("system", instructions);
        var response = await client.CompleteAsync([prompt, .. messages], cancellationToken);
        return ParseFacts(response);
    }

    internal static IReadOnlyList<MemoryInput> ParseFacts(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return [];
        var json = ExtractJsonArrayPayload(response);
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var facts = JsonSerializer.Deserialize<string[]>(json, new JsonSerializerOptions { AllowTrailingCommas = true }) ?? [];
            return facts.Where(fact => !string.IsNullOrWhiteSpace(fact)).Select(fact => new MemoryInput(fact.Trim())).ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal static string ExtractJsonArrayPayload(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
            {
                var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                if (lastFence > firstNewline)
                {
                    trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
                }
            }
        }
        var start = trimmed.IndexOf('[');
        var end = trimmed.LastIndexOf(']');
        return start >= 0 && end > start ? trimmed[start..(end + 1)] : trimmed;
    }
}