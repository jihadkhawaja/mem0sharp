using System.Text.Json;

namespace Mem0Sharp;

public sealed class LlmMemoryExtractor : IMemoryExtractor
{
    private readonly IChatCompletionClient client;

    public LlmMemoryExtractor(IChatCompletionClient client) => this.client = client;

    public async Task<IReadOnlyList<MemoryInput>> ExtractAsync(IReadOnlyList<Message> messages, CancellationToken cancellationToken = default)
    {
        var prompt = new Message("system", "Extract durable user facts from the conversation. Return only a JSON array of strings. Ignore greetings, questions, and temporary requests.");
        var response = await client.CompleteAsync([prompt, .. messages], cancellationToken);
        try
        {
            var facts = JsonSerializer.Deserialize<string[]>(response) ?? [];
            return facts.Where(fact => !string.IsNullOrWhiteSpace(fact)).Select(fact => new MemoryInput(fact.Trim())).ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}