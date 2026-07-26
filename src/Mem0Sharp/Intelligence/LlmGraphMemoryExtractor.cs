using System.Text.Json;

namespace Mem0Sharp;

public sealed class LlmGraphMemoryExtractor : IGraphMemoryExtractor
{
    private readonly IChatCompletionClient client;

    public LlmGraphMemoryExtractor(IChatCompletionClient client) => this.client = client;

    public async Task<IReadOnlyList<ExtractedRelation>> ExtractAsync(string text, CancellationToken cancellationToken = default)
    {
        var response = await client.CompleteAsync(
        [
            new Message("system", "Extract factual relationships. Return only a JSON array of objects with source, relationship, and target string fields."),
            new Message("user", text)
        ], cancellationToken);
        try
        {
            return JsonSerializer.Deserialize<ExtractedRelation[]>(response, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}