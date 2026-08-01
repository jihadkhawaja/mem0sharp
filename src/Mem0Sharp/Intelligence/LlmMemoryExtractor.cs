using System.Text.Json;

namespace Mem0Sharp;

public sealed class LlmMemoryExtractor : IBehaviorAwareMemoryExtractor
{
    private readonly IChatCompletionClient client;

    public LlmMemoryExtractor(IChatCompletionClient client) => this.client = client;

    public async Task<IReadOnlyList<MemoryInput>> ExtractAsync(IReadOnlyList<Message> messages, CancellationToken cancellationToken = default)
        => await ExtractAsync(messages, MemoryBehaviorPrompts.NormalExtraction, cancellationToken);

    public async Task<IReadOnlyList<MemoryInput>> ExtractAsync(IReadOnlyList<Message> messages, MemoryAddOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return await ExtractAsync(messages, MemoryBehaviorPrompts.ForExtraction(options), cancellationToken);
    }

    private async Task<IReadOnlyList<MemoryInput>> ExtractAsync(IReadOnlyList<Message> messages, string instructions, CancellationToken cancellationToken)
    {
        var prompt = new Message("system", instructions);
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