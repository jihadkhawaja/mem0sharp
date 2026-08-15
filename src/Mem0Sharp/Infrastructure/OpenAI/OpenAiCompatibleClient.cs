using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mem0Sharp;

public sealed class OpenAiCompatibleClient : IChatCompletionClient, IEmbeddingGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private readonly string apiKey;
    private readonly string chatModel;
    private readonly string embeddingModel;

    public OpenAiCompatibleClient(HttpClient httpClient, string apiKey, string chatModel = "gpt-5-mini", string embeddingModel = "text-embedding-3-small")
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        this.httpClient = httpClient;
        this.apiKey = apiKey;
        this.chatModel = chatModel;
        this.embeddingModel = embeddingModel;
    }

    public async Task<string> CompleteAsync(IReadOnlyList<Message> messages, CancellationToken cancellationToken = default)
    {
        var endpoint = BuildEndpoint("v1/chat/completions");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new { model = chatModel, messages }, options: JsonOptions)
        };
        AddAuthentication(request);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(JsonOptions, cancellationToken);
        return payload?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
    }

    public async Task<IReadOnlyList<float>> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        var vectors = await GenerateBatchAsync([text], cancellationToken);
        return vectors.Count == 0 ? [] : vectors[0];
    }

    public async Task<IReadOnlyList<IReadOnlyList<float>>> GenerateBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0) return [];
        var endpoint = BuildEndpoint("v1/embeddings");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new { model = embeddingModel, input = texts }, options: JsonOptions)
        };
        AddAuthentication(request);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(JsonOptions, cancellationToken);
        var data = payload?.Data;
        if (data is null || data.Length == 0) return [];
        return data.OrderBy(item => item.Index)
            .Select(item => (IReadOnlyList<float>)(item.Embedding ?? []))
            .ToArray();
    }

    private Uri BuildEndpoint(string relativePath)
    {
        if (httpClient.BaseAddress is null) return new Uri(relativePath, UriKind.Relative);
        var baseStr = httpClient.BaseAddress.AbsoluteUri.TrimEnd('/');
        if (baseStr.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) && relativePath.StartsWith("v1/", StringComparison.OrdinalIgnoreCase))
        {
            relativePath = relativePath[3..];
        }
        return new Uri($"{baseStr}/{relativePath.TrimStart('/')}");
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

    private sealed record ChatCompletionResponse(
        [property: JsonPropertyName("choices")] ChatChoice[]? Choices);

    private sealed record ChatChoice(
        [property: JsonPropertyName("message")] ChatResponseMessage? Message);

    private sealed record ChatResponseMessage(
        [property: JsonPropertyName("content")] string? Content);

    private sealed record EmbeddingResponse(
        [property: JsonPropertyName("data")] EmbeddingItem[]? Data);

    private sealed record EmbeddingItem(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[]? Embedding);
}
