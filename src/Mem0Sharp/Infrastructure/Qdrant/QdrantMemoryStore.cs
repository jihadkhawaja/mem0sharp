using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mem0Sharp;

public sealed class QdrantMemoryStore : IBatchVectorMemoryStore, IBulkMemoryStore, IResettableMemoryStore
{
    private const int ScrollPageSize = 256;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private readonly QdrantMemoryStoreOptions options;
    private readonly string collectionPath;

    public QdrantMemoryStore(HttpClient httpClient, QdrantMemoryStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Endpoint.IsAbsoluteUri) throw new ArgumentException("Qdrant endpoint must be absolute.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.CollectionName)) throw new ArgumentException("CollectionName is required.", nameof(options));
        if (options.EmbeddingDimensions < 1) throw new ArgumentOutOfRangeException(nameof(options));
        this.httpClient = httpClient;
        this.options = options;
        collectionPath = $"collections/{Uri.EscapeDataString(options.CollectionName)}";
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using var probe = await SendAsync(HttpMethod.Get, collectionPath, null, cancellationToken, ensureSuccess: false);
        if (probe.IsSuccessStatusCode) return;
        if (probe.StatusCode != HttpStatusCode.NotFound) await ThrowRequestErrorAsync(probe, cancellationToken);
        using var response = await SendAsync(HttpMethod.Put, collectionPath, new { vectors = new { size = options.EmbeddingDimensions, distance = "Cosine" } }, cancellationToken);
    }

    public Task SaveAsync(Memory memory, CancellationToken cancellationToken = default) => SaveAsync(memory, new float[options.EmbeddingDimensions], cancellationToken);

    public Task SaveAsync(Memory memory, IReadOnlyList<float> embedding, CancellationToken cancellationToken = default) =>
        SaveBatchAsync([new MemoryVectorRecord(memory, embedding)], cancellationToken);

    public async Task SaveBatchAsync(IReadOnlyList<MemoryVectorRecord> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        foreach (var record in records) ValidateEmbedding(record.Embedding);
        if (records.Count == 0) return;
        var points = records.Select(record => new
        {
            id = PointId(record.Memory.Id),
            vector = record.Embedding,
            payload = new { memory = record.Memory }
        }).ToArray();
        using var response = await SendAsync(HttpMethod.Put, $"{collectionPath}/points?wait=true", new { points }, cancellationToken);
    }

    public async Task<Memory?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        using var response = await SendAsync(HttpMethod.Get, $"{collectionPath}/points/{Uri.EscapeDataString(PointId(id))}?with_payload=true", null, cancellationToken, ensureSuccess: false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) await ThrowRequestErrorAsync(response, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        return ReadMemory(payload?["result"]?["payload"]?["memory"]);
    }

    public async IAsyncEnumerable<Memory> GetAllAsync(MemoryFilter? filter = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var point in await ScrollAsync(false, cancellationToken))
        {
            if (MemoryFilterEvaluator.Matches(point.Memory, filter)) yield return point.Memory;
        }
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(IReadOnlyList<float> embedding, MemoryFilter? filter = null, int topK = 5, CancellationToken cancellationToken = default)
    {
        ValidateEmbedding(embedding);
        if (topK < 0) throw new ArgumentOutOfRangeException(nameof(topK));
        var points = await ScrollAsync(true, cancellationToken);
        return Rank(embedding, points, filter, topK);
    }

    public async Task<IReadOnlyList<IReadOnlyList<SearchResult>>> SearchBatchAsync(IReadOnlyList<IReadOnlyList<float>> embeddings, MemoryFilter? filter = null, int topK = 5, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(embeddings);
        foreach (var embedding in embeddings) ValidateEmbedding(embedding);
        if (topK < 0) throw new ArgumentOutOfRangeException(nameof(topK));
        var points = await ScrollAsync(true, cancellationToken);
        return embeddings.Select(embedding => Rank(embedding, points, filter, topK)).ToArray();
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await DeleteIdsAsync([id], cancellationToken);
    }

    public async Task<int> DeleteAllAsync(MemoryFilter? filter = null, CancellationToken cancellationToken = default)
    {
        var ids = new List<string>();
        await foreach (var memory in GetAllAsync(filter, cancellationToken)) ids.Add(memory.Id);
        await DeleteIdsAsync(ids, cancellationToken);
        return ids.Count;
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        using var deletion = await SendAsync(HttpMethod.Delete, collectionPath, null, cancellationToken, ensureSuccess: false);
        if (!deletion.IsSuccessStatusCode && deletion.StatusCode != HttpStatusCode.NotFound) await ThrowRequestErrorAsync(deletion, cancellationToken);
        using var creation = await SendAsync(HttpMethod.Put, collectionPath, new { vectors = new { size = options.EmbeddingDimensions, distance = "Cosine" } }, cancellationToken);
    }

    private async Task DeleteIdsAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0) return;
        using var response = await SendAsync(HttpMethod.Post, $"{collectionPath}/points/delete?wait=true", new { points = ids.Select(PointId).ToArray() }, cancellationToken);
    }

    private async Task<IReadOnlyList<QdrantPoint>> ScrollAsync(bool withVectors, CancellationToken cancellationToken)
    {
        var points = new List<QdrantPoint>();
        JsonNode? offset = null;
        do
        {
            var body = new JsonObject
            {
                ["limit"] = ScrollPageSize,
                ["with_payload"] = true,
                ["with_vector"] = withVectors
            };
            if (offset is not null) body["offset"] = offset.DeepClone();
            using var response = await SendAsync(HttpMethod.Post, $"{collectionPath}/points/scroll", body, cancellationToken);
            var payload = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
            var result = payload?["result"];
            foreach (var point in result?["points"]?.AsArray() ?? [])
            {
                var memory = ReadMemory(point?["payload"]?["memory"]);
                if (memory is null) continue;
                var vector = point?["vector"]?.AsArray().Select(value => value?.GetValue<float>() ?? 0).ToArray();
                points.Add(new QdrantPoint(memory, vector));
            }
            offset = result?["next_page_offset"]?.DeepClone();
        }
        while (offset is not null);
        return points;
    }

    private static IReadOnlyList<SearchResult> Rank(IReadOnlyList<float> embedding, IReadOnlyList<QdrantPoint> points, MemoryFilter? filter, int topK) =>
        points.Where(point => point.Vector is not null && MemoryFilterEvaluator.Matches(point.Memory, filter))
            .Select(point => new SearchResult(point.Memory, CosineSimilarity(embedding, point.Vector!)))
            .OrderByDescending(result => result.Score)
            .Take(topK)
            .ToArray();

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken, bool ensureSuccess = true)
    {
        var endpoint = new Uri(options.Endpoint.AbsoluteUri.TrimEnd('/') + "/" + path);
        var request = new HttpRequestMessage(method, endpoint);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (!string.IsNullOrWhiteSpace(options.ApiKey)) request.Headers.Add("api-key", options.ApiKey);
        var response = await httpClient.SendAsync(request, cancellationToken);
        request.Dispose();
        if (ensureSuccess && !response.IsSuccessStatusCode)
        {
            await ThrowRequestErrorAsync(response, cancellationToken);
        }
        return response;
    }

    private static async Task ThrowRequestErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException($"Qdrant request failed with {(int)response.StatusCode}: {body}", null, response.StatusCode);
    }

    private void ValidateEmbedding(IReadOnlyList<float> embedding)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        if (embedding.Count != options.EmbeddingDimensions) throw new ArgumentException("Embedding dimensions do not match the Qdrant collection.", nameof(embedding));
    }

    private static string PointId(string memoryId) => Guid.TryParse(memoryId, out var id) ? id.ToString("D") : memoryId;
    private static Memory? ReadMemory(JsonNode? node) => node?.Deserialize<Memory>(JsonOptions);

    private static double CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        double dot = 0;
        double leftMagnitude = 0;
        double rightMagnitude = 0;
        for (var index = 0; index < left.Count; index++)
        {
            dot += left[index] * right[index];
            leftMagnitude += left[index] * left[index];
            rightMagnitude += right[index] * right[index];
        }
        return leftMagnitude == 0 || rightMagnitude == 0 ? 0 : dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
    }

    private sealed record QdrantPoint(Memory Memory, IReadOnlyList<float>? Vector);
}