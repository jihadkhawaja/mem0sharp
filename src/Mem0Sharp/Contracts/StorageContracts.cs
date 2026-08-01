namespace Mem0Sharp;

public interface IMemoryStore
{
    Task SaveAsync(Memory memory, CancellationToken cancellationToken = default);
    Task<Memory?> GetAsync(string id, CancellationToken cancellationToken = default);
    IAsyncEnumerable<Memory> GetAllAsync(MemoryFilter? filter = null, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}

public interface IVectorMemoryStore : IMemoryStore
{
    Task SaveAsync(Memory memory, IReadOnlyList<float> embedding, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SearchResult>> SearchAsync(IReadOnlyList<float> embedding, MemoryFilter? filter = null, int topK = 5, CancellationToken cancellationToken = default);
}

public interface IBulkMemoryStore : IMemoryStore
{
    Task<int> DeleteAllAsync(MemoryFilter? filter = null, CancellationToken cancellationToken = default);
}

public interface IBatchMemoryStore : IMemoryStore
{
    Task SaveBatchAsync(IReadOnlyList<Memory> memories, CancellationToken cancellationToken = default);
}

public interface IBatchVectorMemoryStore : IVectorMemoryStore
{
    Task SaveBatchAsync(IReadOnlyList<MemoryVectorRecord> records, CancellationToken cancellationToken = default);
    async Task<IReadOnlyList<IReadOnlyList<SearchResult>>> SearchBatchAsync(IReadOnlyList<IReadOnlyList<float>> embeddings, MemoryFilter? filter = null, int topK = 5, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(embeddings);
        var results = new List<IReadOnlyList<SearchResult>>(embeddings.Count);
        foreach (var embedding in embeddings) results.Add(await SearchAsync(embedding, filter, topK, cancellationToken));
        return results;
    }
}

public interface IMemoryHistoryStore : IMemoryStore
{
    Task SaveHistoryAsync(MemoryHistoryEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryHistoryEntry>> GetHistoryAsync(string memoryId, CancellationToken cancellationToken = default);
}

public interface IResettableMemoryStore : IMemoryStore
{
    Task ResetAsync(CancellationToken cancellationToken = default);
}