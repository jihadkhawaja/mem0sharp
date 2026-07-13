using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mem0Sharp;

public interface IChatCompletionClient
{
    Task<string> CompleteAsync(IReadOnlyList<Message> messages, CancellationToken cancellationToken = default);
}

public sealed class OpenAiCompatibleClient : IChatCompletionClient, IEmbeddingGenerator
{
    private readonly HttpClient httpClient;
    private readonly string apiKey;
    private readonly string chatModel;
    private readonly string embeddingModel;

    public OpenAiCompatibleClient(HttpClient httpClient, string apiKey, string chatModel = "gpt-5-mini", string embeddingModel = "text-embedding-3-small")
    {
        this.httpClient = httpClient;
        this.apiKey = apiKey;
        this.chatModel = chatModel;
        this.embeddingModel = embeddingModel;
    }

    public async Task<string> CompleteAsync(IReadOnlyList<Message> messages, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = JsonContent.Create(new { model = chatModel, messages })
        };
        AddAuthentication(request);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        return payload?["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? string.Empty;
    }

    public async Task<IReadOnlyList<float>> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/embeddings")
        {
            Content = JsonContent.Create(new { model = embeddingModel, input = text })
        };
        AddAuthentication(request);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        var values = payload?["data"]?[0]?["embedding"]?.AsArray();
        return values is null ? [] : values.Select(value => value?.GetValue<float>() ?? 0).ToArray();
    }

    private void AddAuthentication(HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException($"OpenAI-compatible request failed with {(int)response.StatusCode}: {body}");
    }
}

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
