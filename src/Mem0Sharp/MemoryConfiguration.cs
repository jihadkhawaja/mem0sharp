using System.Collections.Concurrent;

namespace Mem0Sharp;

public sealed record MemoryServiceConfiguration
{
    public IMemoryStore? Store { get; init; }
    public IEmbeddingGenerator? Embeddings { get; init; }
    public IMemoryExtractor? Extractor { get; init; }
    public MemoryOptions? Options { get; init; }
    public IMemoryReranker? Reranker { get; init; }
    public IMemoryConflictResolver? ConflictResolver { get; init; }
    public IProceduralMemoryGenerator? ProceduralMemoryGenerator { get; init; }
    public IEntityExtractor? EntityExtractor { get; init; }
    public IEntityStore? EntityStore { get; init; }
    public IGraphMemoryExtractor? GraphExtractor { get; init; }
    public IGraphMemoryStore? GraphStore { get; init; }
    public IMemoryTelemetry? Telemetry { get; init; }

    public IMemoryService CreateService()
    {
        IMemoryService service = new MemoryService(Store, Embeddings, Extractor, Options, Reranker, ConflictResolver, ProceduralMemoryGenerator, EntityExtractor, EntityStore, GraphExtractor, GraphStore);
        return Telemetry is null ? service : new TelemetryMemoryService(service, Telemetry);
    }
}

public sealed class InMemoryTelemetryCollector : IMemoryTelemetry
{
    private readonly ConcurrentQueue<MemoryTelemetryEvent> events = new();

    public IReadOnlyList<MemoryTelemetryEvent> Events => events.ToArray();

    public Task CaptureAsync(MemoryTelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        events.Enqueue(telemetryEvent);
        return Task.CompletedTask;
    }
}

public sealed class TelemetryMemoryService : IMemoryService
{
    private readonly IMemoryService inner;
    private readonly IMemoryTelemetry telemetry;

    public TelemetryMemoryService(IMemoryService inner, IMemoryTelemetry telemetry)
    {
        this.inner = inner;
        this.telemetry = telemetry;
    }

    public Task<AddResult> AddAsync(string text, string userId = "default_user", string? agentId = null, string? runId = null, MemoryScope scope = MemoryScope.User, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default) =>
        CaptureAsync("mem0.add", () => inner.AddAsync(text, userId, agentId, runId, scope, metadata, cancellationToken), new Dictionary<string, object?> { ["input_type"] = "text", ["scope"] = scope.ToString() }, cancellationToken);

    public Task<AddResult> AddAsync(IEnumerable<Message> messages, string userId = "default_user", string? agentId = null, string? runId = null, MemoryScope scope = MemoryScope.User, CancellationToken cancellationToken = default) =>
        CaptureAsync("mem0.add", () => inner.AddAsync(messages, userId, agentId, runId, scope, cancellationToken), new Dictionary<string, object?> { ["input_type"] = "messages", ["scope"] = scope.ToString() }, cancellationToken);

    public Task<AddResult> AddAsync(string text, MemoryAddOptions options, CancellationToken cancellationToken = default) =>
        CaptureAsync("mem0.add", () => inner.AddAsync(text, options, cancellationToken), new Dictionary<string, object?> { ["input_type"] = "text", ["infer"] = options.Infer }, cancellationToken);

    public Task<AddResult> AddAsync(IEnumerable<Message> messages, MemoryAddOptions options, CancellationToken cancellationToken = default) =>
        CaptureAsync("mem0.add", () => inner.AddAsync(messages, options, cancellationToken), new Dictionary<string, object?> { ["input_type"] = "messages", ["infer"] = options.Infer }, cancellationToken);

    public Task<AddResult> AddManyAsync(IEnumerable<string> texts, MemoryAddOptions? options = null, CancellationToken cancellationToken = default) =>
        CaptureAsync("mem0.add_many", () => inner.AddManyAsync(texts, options, cancellationToken), cancellationToken: cancellationToken);

    public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, MemoryFilter? filter = null, int? topK = null, CancellationToken cancellationToken = default) =>
        CaptureAsync("mem0.search", () => inner.SearchAsync(query, filter, topK, cancellationToken), new Dictionary<string, object?> { ["top_k"] = topK }, cancellationToken);

    public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, MemorySearchOptions options, CancellationToken cancellationToken = default) =>
        CaptureAsync("mem0.search", () => inner.SearchAsync(query, options, cancellationToken), new Dictionary<string, object?> { ["top_k"] = options.TopK, ["rerank"] = options.Rerank, ["explain"] = options.Explain }, cancellationToken);

    public Task<IReadOnlyList<IReadOnlyList<SearchResult>>> SearchManyAsync(IEnumerable<string> queries, MemoryFilter? filter = null, int? topK = null, CancellationToken cancellationToken = default) =>
        CaptureAsync("mem0.search_many", () => inner.SearchManyAsync(queries, filter, topK, cancellationToken), cancellationToken: cancellationToken);

    public Task<Memory?> GetAsync(string id, CancellationToken cancellationToken = default) => CaptureAsync("mem0.get", () => inner.GetAsync(id, cancellationToken), cancellationToken: cancellationToken);
    public Task<IReadOnlyList<Memory>> GetAllAsync(MemoryFilter? filter = null, CancellationToken cancellationToken = default) => CaptureAsync("mem0.get_all", () => inner.GetAllAsync(filter, cancellationToken), cancellationToken: cancellationToken);
    public Task<MemoryPage> GetPageAsync(MemoryPageOptions options, MemoryFilter? filter = null, CancellationToken cancellationToken = default) => CaptureAsync("mem0.get_page", () => inner.GetPageAsync(options, filter, cancellationToken), cancellationToken: cancellationToken);
    public Task<Memory> UpdateAsync(string id, string text, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default) => CaptureAsync("mem0.update", () => inner.UpdateAsync(id, text, metadata, cancellationToken), cancellationToken: cancellationToken);
    public Task<Memory> UpdateAsync(string id, MemoryUpdate update, CancellationToken cancellationToken = default) => CaptureAsync("mem0.update", () => inner.UpdateAsync(id, update, cancellationToken), cancellationToken: cancellationToken);
    public Task DeleteAsync(string id, CancellationToken cancellationToken = default) => CaptureAsync("mem0.delete", () => inner.DeleteAsync(id, cancellationToken), cancellationToken: cancellationToken);
    public Task<int> DeleteAllAsync(MemoryFilter? filter = null, CancellationToken cancellationToken = default) => CaptureAsync("mem0.delete_all", () => inner.DeleteAllAsync(filter, cancellationToken), cancellationToken: cancellationToken);
    public Task<IReadOnlyList<MemoryHistoryEntry>> GetHistoryAsync(string id, CancellationToken cancellationToken = default) => CaptureAsync("mem0.history", () => inner.GetHistoryAsync(id, cancellationToken), cancellationToken: cancellationToken);
    public Task ResetAsync(CancellationToken cancellationToken = default) => CaptureAsync("mem0.reset", () => inner.ResetAsync(cancellationToken), cancellationToken: cancellationToken);
    public Task<IReadOnlyList<MemoryRelation>> GetRelationsAsync(string? query = null, CancellationToken cancellationToken = default) => CaptureAsync("mem0.graph", () => inner.GetRelationsAsync(query, cancellationToken), cancellationToken: cancellationToken);

    private async Task<T> CaptureAsync<T>(string name, Func<Task<T>> operation, IReadOnlyDictionary<string, object?>? properties = null, CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;
        try
        {
            var result = await operation();
            await telemetry.CaptureAsync(new MemoryTelemetryEvent(name, started, Merge(properties, true)), cancellationToken);
            return result;
        }
        catch
        {
            await telemetry.CaptureAsync(new MemoryTelemetryEvent(name, started, Merge(properties, false)), cancellationToken);
            throw;
        }
    }

    private async Task CaptureAsync(string name, Func<Task> operation, IReadOnlyDictionary<string, object?>? properties = null, CancellationToken cancellationToken = default)
    {
        await CaptureAsync(name, async () => { await operation(); return true; }, properties, cancellationToken);
    }

    private static IReadOnlyDictionary<string, object?> Merge(IReadOnlyDictionary<string, object?>? properties, bool success)
    {
        var result = new Dictionary<string, object?>(properties ?? new Dictionary<string, object?>()) { ["success"] = success, ["sync_type"] = "async" };
        return result;
    }
}

public sealed class SynchronousMemoryService
{
    private readonly IMemoryService service;

    public SynchronousMemoryService(IMemoryService service) => this.service = service;

    public AddResult Add(string text, MemoryAddOptions? options = null) => options is null ? service.AddAsync(text).GetAwaiter().GetResult() : service.AddAsync(text, options).GetAwaiter().GetResult();
    public AddResult Add(IEnumerable<Message> messages, MemoryAddOptions? options = null) => options is null ? service.AddAsync(messages).GetAwaiter().GetResult() : service.AddAsync(messages, options).GetAwaiter().GetResult();
    public AddResult AddMany(IEnumerable<string> texts, MemoryAddOptions? options = null) => service.AddManyAsync(texts, options).GetAwaiter().GetResult();
    public IReadOnlyList<SearchResult> Search(string query, MemorySearchOptions? options = null) => options is null ? service.SearchAsync(query).GetAwaiter().GetResult() : service.SearchAsync(query, options).GetAwaiter().GetResult();
    public Memory? Get(string id) => service.GetAsync(id).GetAwaiter().GetResult();
    public IReadOnlyList<Memory> GetAll(MemoryFilter? filter = null) => service.GetAllAsync(filter).GetAwaiter().GetResult();
    public Memory Update(string id, MemoryUpdate update) => service.UpdateAsync(id, update).GetAwaiter().GetResult();
    public void Delete(string id) => service.DeleteAsync(id).GetAwaiter().GetResult();
    public int DeleteAll(MemoryFilter? filter = null) => service.DeleteAllAsync(filter).GetAwaiter().GetResult();
    public IReadOnlyList<MemoryHistoryEntry> History(string id) => service.GetHistoryAsync(id).GetAwaiter().GetResult();
    public void Reset() => service.ResetAsync().GetAwaiter().GetResult();
}