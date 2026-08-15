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
        return ParseRelations(response);
    }

    internal static IReadOnlyList<ExtractedRelation> ParseRelations(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return [];
        var json = LlmMemoryExtractor.ExtractJsonArrayPayload(response);
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<ExtractedRelation[]>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, AllowTrailingCommas = true }) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}