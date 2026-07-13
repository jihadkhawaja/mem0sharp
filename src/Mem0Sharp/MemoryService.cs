namespace Mem0Sharp;

public sealed class MemoryService : IMemoryService
{
    private readonly IMemoryStore store;
    private readonly IEmbeddingGenerator embeddings;
    private readonly IMemoryExtractor extractor;
    private readonly MemoryOptions options;
    private readonly Dictionary<string, float[]> vectors = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim indexLock = new(1, 1);

    public MemoryService(IMemoryStore? store = null, IEmbeddingGenerator? embeddings = null, IMemoryExtractor? extractor = null, MemoryOptions? options = null)
    {
        this.store = store ?? new InMemoryStore();
        this.embeddings = embeddings ?? new LocalEmbeddingGenerator();
        this.extractor = extractor ?? new BasicMemoryExtractor();
        this.options = options ?? new MemoryOptions();
    }

    public async Task<AddResult> AddAsync(string text, string userId = "default_user", string? agentId = null, string? runId = null, MemoryScope scope = MemoryScope.User, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return await SaveInputsAsync([new MemoryInput(text, scope, metadata)], userId, agentId, runId, cancellationToken);
    }

    public async Task<AddResult> AddAsync(IEnumerable<Message> messages, string userId = "default_user", string? agentId = null, string? runId = null, MemoryScope scope = MemoryScope.User, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var inputs = await extractor.ExtractAsync(messages.ToArray(), cancellationToken);
        return await SaveInputsAsync(inputs.Select(input => input with { Scope = scope }), userId, agentId, runId, cancellationToken);
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, MemoryFilter? filter = null, int? topK = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var queryVector = await embeddings.GenerateAsync(query, cancellationToken);
        if (store is IVectorMemoryStore vectorStore)
        {
            return await vectorStore.SearchAsync(queryVector, filter, topK ?? options.DefaultTopK, cancellationToken);
        }
        var candidates = new List<Memory>();
        await foreach (var memory in store.GetAllAsync(filter, cancellationToken))
        {
            if (candidates.Count == options.MaxCandidateCount) break;
            candidates.Add(memory);
        }
        var results = new List<SearchResult>(candidates.Count);
        foreach (var memory in candidates)
        {
            var score = CosineSimilarity(queryVector, await GetVectorAsync(memory, cancellationToken));
            if (score >= options.MinimumScore) results.Add(new SearchResult(memory, score));
        }
        return results.OrderByDescending(result => result.Score).Take(topK ?? options.DefaultTopK).ToArray();
    }

    public async Task<IReadOnlyList<IReadOnlyList<SearchResult>>> SearchManyAsync(IEnumerable<string> queries, MemoryFilter? filter = null, int? topK = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queries);
        var results = new List<IReadOnlyList<SearchResult>>();
        foreach (var query in queries) results.Add(await SearchAsync(query, filter, topK, cancellationToken));
        return results;
    }

    public Task<Memory?> GetAsync(string id, CancellationToken cancellationToken = default) => store.GetAsync(id, cancellationToken);

    public async Task<IReadOnlyList<Memory>> GetAllAsync(MemoryFilter? filter = null, CancellationToken cancellationToken = default)
    {
        var result = new List<Memory>();
        await foreach (var memory in store.GetAllAsync(filter, cancellationToken)) result.Add(memory);
        return result;
    }

    public async Task<Memory> UpdateAsync(string id, string text, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var existing = await store.GetAsync(id, cancellationToken) ?? throw new KeyNotFoundException($"Memory '{id}' was not found.");
        var updated = existing with { Text = text, Metadata = metadata ?? existing.Metadata, UpdatedAt = DateTimeOffset.UtcNow };
        var updatedVector = await embeddings.GenerateAsync(text, cancellationToken);
        if (store is IVectorMemoryStore vectorStore)
        {
            await vectorStore.SaveAsync(updated, updatedVector, cancellationToken);
        }
        else
        {
            await store.SaveAsync(updated, cancellationToken);
        }
        await indexLock.WaitAsync(cancellationToken);
        try { vectors[id] = updatedVector.ToArray(); }
        finally { indexLock.Release(); }
        return updated;
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await store.DeleteAsync(id, cancellationToken);
        await indexLock.WaitAsync(cancellationToken);
        try { vectors.Remove(id); }
        finally { indexLock.Release(); }
    }

    public async Task<int> DeleteAllAsync(MemoryFilter? filter = null, CancellationToken cancellationToken = default)
    {
        if (store is IBulkMemoryStore bulkStore) return await bulkStore.DeleteAllAsync(filter, cancellationToken);
        var memories = await GetAllAsync(filter, cancellationToken);
        foreach (var memory in memories) await DeleteAsync(memory.Id, cancellationToken);
        return memories.Count;
    }

    private async Task<AddResult> SaveInputsAsync(IEnumerable<MemoryInput> inputs, string userId, string? agentId, string? runId, CancellationToken cancellationToken)
    {
        var saved = new List<Memory>();
        foreach (var input in inputs.Where(item => !string.IsNullOrWhiteSpace(item.Text)))
        {
            var now = DateTimeOffset.UtcNow;
            var memory = new Memory { Id = Guid.NewGuid().ToString("N"), Text = input.Text.Trim(), UserId = userId, AgentId = agentId, RunId = runId, Scope = input.Scope, Metadata = input.Metadata ?? new Dictionary<string, string>(), CreatedAt = now, UpdatedAt = now };
            var vector = await embeddings.GenerateAsync(memory.Text, cancellationToken);
            if (store is IVectorMemoryStore vectorStore)
            {
                await vectorStore.SaveAsync(memory, vector, cancellationToken);
            }
            else
            {
                await store.SaveAsync(memory, cancellationToken);
            }
            await indexLock.WaitAsync(cancellationToken);
            try { vectors[memory.Id] = vector.ToArray(); }
            finally { indexLock.Release(); }
            saved.Add(memory);
        }
        return new AddResult(saved);
    }

    private async Task<float[]> GetVectorAsync(Memory memory, CancellationToken cancellationToken)
    {
        await indexLock.WaitAsync(cancellationToken);
        try
        {
            if (vectors.TryGetValue(memory.Id, out var vector)) return vector;
            vector = (await embeddings.GenerateAsync(memory.Text, cancellationToken)).ToArray();
            vectors[memory.Id] = vector;
            return vector;
        }
        finally { indexLock.Release(); }
    }

    private static double CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        if (left.Count != right.Count) return 0;
        double dot = 0, leftNorm = 0, rightNorm = 0;
        for (var index = 0; index < left.Count; index++) { dot += left[index] * right[index]; leftNorm += left[index] * left[index]; rightNorm += right[index] * right[index]; }
        return leftNorm == 0 || rightNorm == 0 ? 0 : dot / Math.Sqrt(leftNorm * rightNorm);
    }
}