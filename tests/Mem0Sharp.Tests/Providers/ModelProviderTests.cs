using System.Net;
using System.Text;
using System.Text.Json;
using Mem0Sharp;
using Xunit;

namespace Mem0Sharp.Tests;

public sealed class ModelProviderTests
{
    [Fact]
    public async Task AnthropicClientMapsSystemMessagesAndAuthentication()
    {
        var handler = new RecordingHandler(async request =>
        {
            Assert.Equal("https://api.anthropic.com/v1/messages", request.RequestUri!.AbsoluteUri);
            Assert.Equal("secret", Assert.Single(request.Headers.GetValues("x-api-key")));
            Assert.Equal("2023-06-01", Assert.Single(request.Headers.GetValues("anthropic-version")));
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStreamAsync());
            Assert.Equal("instructions", body.RootElement.GetProperty("system").GetString());
            Assert.Equal(1, body.RootElement.GetProperty("messages").GetArrayLength());
            return JsonResponse("""{"content":[{"type":"text","text":"answer"}]}""");
        });
        var client = new AnthropicClient(new HttpClient(handler), "secret");

        var result = await client.CompleteAsync([new Message("system", "instructions"), new Message("user", "question")]);

        Assert.Equal("answer", result);
    }

    [Fact]
    public async Task OllamaClientSupportsChatAndBatchEmbeddings()
    {
        var requests = new List<string>();
        var handler = new RecordingHandler(async request =>
        {
            requests.Add(request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStreamAsync());
            if (request.RequestUri.AbsolutePath == "/api/chat")
            {
                Assert.False(body.RootElement.GetProperty("stream").GetBoolean());
                return JsonResponse("""{"message":{"role":"assistant","content":"local answer"}}""");
            }
            Assert.Equal(2, body.RootElement.GetProperty("input").GetArrayLength());
            return JsonResponse("""{"embeddings":[[1,0],[0,1]]}""");
        });
        var client = new OllamaClient(new HttpClient(handler));

        var answer = await client.CompleteAsync([new Message("user", "question")]);
        var vectors = await client.GenerateBatchAsync(["first", "second"]);

        Assert.Equal("local answer", answer);
        Assert.Equal(["/api/chat", "/api/embed"], requests);
        Assert.Equal(2, vectors.Count);
        Assert.All(vectors, vector => Assert.Equal(2, vector.Count));
    }

    [Fact]
    public async Task AnthropicClientIncludesProviderErrorBody()
    {
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("rate limited")
        }));
        var client = new AnthropicClient(new HttpClient(handler), "secret");

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.CompleteAsync([new Message("user", "question")]));

        Assert.Contains("429", error.Message);
        Assert.Contains("rate limited", error.Message);
    }

    [Fact]
    public async Task OllamaClientRejectsInconsistentEmbeddingDimensions()
    {
        var handler = new RecordingHandler(_ => Task.FromResult(JsonResponse("""{"embeddings":[[1,0],[1,0,0]]}""")));
        var client = new OllamaClient(new HttpClient(handler));

        await Assert.ThrowsAsync<InvalidDataException>(() => client.GenerateBatchAsync(["first", "second"]));
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}