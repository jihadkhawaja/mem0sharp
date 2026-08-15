using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mem0Sharp;

public sealed class AnthropicClient : IChatCompletionClient
{
    private static readonly Uri DefaultEndpoint = new("https://api.anthropic.com/v1/messages");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private readonly string apiKey;
    private readonly string model;
    private readonly int maxTokens;
    private readonly Uri endpoint;

    public AnthropicClient(HttpClient httpClient, string apiKey, string model = "claude-sonnet-4-5", int maxTokens = 1024, Uri? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (maxTokens < 1) throw new ArgumentOutOfRangeException(nameof(maxTokens));
        this.httpClient = httpClient;
        this.apiKey = apiKey;
        this.model = model;
        this.maxTokens = maxTokens;
        this.endpoint = endpoint ?? DefaultEndpoint;
    }

    public async Task<string> CompleteAsync(IReadOnlyList<Message> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var system = string.Join("\n\n", messages.Where(message => string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase)).Select(message => message.Content));
        var conversation = messages.Where(message => !string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase))
            .Select(message => new { role = message.Role.ToLowerInvariant(), content = message.Content })
            .ToArray();
        if (conversation.Length == 0) throw new ArgumentException("Anthropic requires at least one non-system message.", nameof(messages));

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new { model, max_tokens = maxTokens, system = string.IsNullOrEmpty(system) ? null : system, messages = conversation }, options: JsonOptions)
        };
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<AnthropicMessageResponse>(JsonOptions, cancellationToken);
        if (payload?.Content is null || payload.Content.Length == 0) return string.Empty;
        return string.Concat(payload.Content
            .Where(block => string.Equals(block.Type, "text", StringComparison.OrdinalIgnoreCase))
            .Select(block => block.Text ?? string.Empty));
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException($"Anthropic request failed with {(int)response.StatusCode}: {body}");
    }

    private sealed record AnthropicMessageResponse(
        [property: JsonPropertyName("content")] AnthropicContentBlock[]? Content);

    private sealed record AnthropicContentBlock(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("text")] string? Text);
}