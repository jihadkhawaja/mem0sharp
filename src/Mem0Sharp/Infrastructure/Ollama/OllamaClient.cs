using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Mem0Sharp;

public sealed class OllamaClient : IChatCompletionClient, IBatchEmbeddingGenerator
{
    private readonly HttpClient httpClient;
    private readonly string chatModel;
    private readonly string embeddingModel;
    private readonly Uri chatEndpoint;
    private readonly Uri embeddingEndpoint;

    public OllamaClient(HttpClient httpClient, string chatModel = "llama3.2", string embeddingModel = "nomic-embed-text", Uri? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(chatModel);
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingModel);
        var baseEndpoint = endpoint ?? new Uri("http://localhost:11434/");
        if (!baseEndpoint.IsAbsoluteUri) throw new ArgumentException("Ollama endpoint must be absolute.", nameof(endpoint));
        this.httpClient = httpClient;
        this.chatModel = chatModel;
        this.embeddingModel = embeddingModel;
        chatEndpoint = new Uri(baseEndpoint, "api/chat");
        embeddingEndpoint = new Uri(baseEndpoint, "api/embed");
    }

    public async Task<string> CompleteAsync(IReadOnlyList<Message> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        using var request = new HttpRequestMessage(HttpMethod.Post, chatEndpoint)
        {
            Content = JsonContent.Create(new { model = chatModel, messages, stream = false })
        };
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        return payload?["message"]?["content"]?.GetValue<string>() ?? string.Empty;
    }

    public async Task<IReadOnlyList<float>> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        var vectors = await GenerateBatchAsync([text], cancellationToken);
        return vectors.Count == 0 ? [] : vectors[0];
    }

    public async Task<IReadOnlyList<IReadOnlyList<float>>> GenerateBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Count == 0) return [];
        using var request = new HttpRequestMessage(HttpMethod.Post, embeddingEndpoint)
        {
            Content = JsonContent.Create(new { model = embeddingModel, input = texts })
        };
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        var embeddings = payload?["embeddings"]?.AsArray();
        if (embeddings is null) throw new InvalidDataException("Ollama returned no embeddings.");
        var vectors = embeddings.Select(vector => (IReadOnlyList<float>)(vector?.AsArray().Select(value => value?.GetValue<float>() ?? 0).ToArray() ?? [])).ToArray();
        if (vectors.Length != texts.Count) throw new InvalidDataException("Ollama returned a different number of embeddings than input texts.");
        if (vectors.Select(vector => vector.Count).Distinct().Count() > 1) throw new InvalidDataException("Ollama returned inconsistent embedding dimensions.");
        return vectors;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException($"Ollama request failed with {(int)response.StatusCode}: {body}");
    }
}