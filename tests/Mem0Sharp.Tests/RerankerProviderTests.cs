using System.Net;
using System.Text;
using System.Text.Json;
using Mem0Sharp;
using Xunit;

namespace Mem0Sharp.Tests;

public sealed class RerankerProviderTests
{
    [Fact]
    public async Task CohereRerankerUsesOfficialProtocolAndMapsResults()
    {
        var handler = new RecordingHandler(async request =>
        {
            Assert.Equal("https://api.cohere.com/v1/rerank", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("secret", request.Headers.Authorization.Parameter);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStreamAsync());
            Assert.Equal("rerank-v3.5", body.RootElement.GetProperty("model").GetString());
            Assert.Equal(2, body.RootElement.GetProperty("top_n").GetInt32());
            Assert.Equal(2, body.RootElement.GetProperty("documents").GetArrayLength());
            return JsonResponse("""{"results":[{"index":1,"relevance_score":0.9},{"index":0,"relevance_score":0.2}]}""");
        });
        var reranker = new CohereReranker(new HttpClient(handler), "secret");

        var results = await reranker.RerankAsync("query", Candidates(), 2);

        Assert.Equal(["second", "first"], results.Select(result => result.Memory.Id));
        Assert.Equal(0.9, results[0].Score, 10);
        Assert.Equal(0.9, results[0].ScoreDetails!.Reranker!.Value, 10);
    }

    [Fact]
    public async Task ZeroEntropyRerankerUsesOfficialProtocolAndMapsResults()
    {
        var handler = new RecordingHandler(async request =>
        {
            Assert.Equal("https://api.zeroentropy.dev/v1/models/rerank", request.RequestUri!.AbsoluteUri);
            Assert.Equal("secret", request.Headers.Authorization!.Parameter);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStreamAsync());
            Assert.Equal("zerank-1", body.RootElement.GetProperty("model").GetString());
            Assert.Equal("query", body.RootElement.GetProperty("query").GetString());
            return JsonResponse("""{"results":[{"index":1,"relevance_score":0.8},{"index":0,"relevance_score":0.1}]}""");
        });
        var reranker = new ZeroEntropyReranker(new HttpClient(handler), "secret");

        var results = await reranker.RerankAsync("query", Candidates(), 1);

        var result = Assert.Single(results);
        Assert.Equal("second", result.Memory.Id);
        Assert.Equal(0.8, result.ScoreDetails!.Reranker!.Value, 10);
    }

    [Fact]
    public async Task CrossEncoderRerankerNormalizesLogitsAndOrdersResults()
    {
        var reranker = new CrossEncoderReranker(new StubCrossEncoderScorer([0, 2]));

        var results = await reranker.RerankAsync("query", Candidates(), 2);

        Assert.Equal(["second", "first"], results.Select(result => result.Memory.Id));
        Assert.Equal(1 / (1 + Math.Exp(-2)), results[0].Score, 10);
        Assert.Equal(0.5, results[1].ScoreDetails!.Reranker!.Value, 10);
    }

    private static IReadOnlyList<SearchResult> Candidates() =>
    [
        new SearchResult(new Memory { Id = "first", Text = "first document", UserId = "user" }, 0.7),
        new SearchResult(new Memory { Id = "second", Text = "second document", UserId = "user" }, 0.6)
    ];

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }

    private sealed class StubCrossEncoderScorer(IReadOnlyList<double> scores) : ICrossEncoderScorer
    {
        public Task<IReadOnlyList<double>> ScoreAsync(string query, IReadOnlyList<string> documents, CancellationToken cancellationToken = default) => Task.FromResult(scores);
    }
}