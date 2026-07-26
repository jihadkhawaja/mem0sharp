namespace Mem0Sharp;

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