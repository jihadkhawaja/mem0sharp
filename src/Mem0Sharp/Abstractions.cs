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

public interface IEmbeddingGenerator
{
    Task<IReadOnlyList<float>> GenerateAsync(string text, CancellationToken cancellationToken = default);
}

public interface IBatchEmbeddingGenerator : IEmbeddingGenerator
{
    Task<IReadOnlyList<IReadOnlyList<float>>> GenerateBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
}

public interface IMemoryExtractor
{
    Task<IReadOnlyList<MemoryInput>> ExtractAsync(IReadOnlyList<Message> messages, CancellationToken cancellationToken = default);
}

public interface IMemoryReranker
{
    Task<IReadOnlyList<SearchResult>> RerankAsync(string query, IReadOnlyList<SearchResult> candidates, int topK, CancellationToken cancellationToken = default);
}

public interface IMemoryConflictResolver
{
    Task<IReadOnlyList<MemoryDecision>> ResolveAsync(IReadOnlyList<Message> messages, IReadOnlyList<Memory> existingMemories, MemoryAddOptions options, CancellationToken cancellationToken = default);
}

public interface IProceduralMemoryGenerator
{
    Task<string> GenerateAsync(IReadOnlyList<Message> messages, string? prompt = null, CancellationToken cancellationToken = default);
}

public interface IEntityExtractor
{
    Task<IReadOnlyList<ExtractedEntity>> ExtractAsync(string text, CancellationToken cancellationToken = default);
}

public interface IEntityStore
{
    Task UpsertLinksAsync(IReadOnlyList<ExtractedEntity> entities, string memoryId, CancellationToken cancellationToken = default);
    Task RemoveMemoryAsync(string memoryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, double>> GetMemoryBoostsAsync(IReadOnlyList<ExtractedEntity> entities, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryEntity>> GetAllAsync(CancellationToken cancellationToken = default);
    Task ResetAsync(CancellationToken cancellationToken = default);
}

public interface IGraphMemoryExtractor
{
    Task<IReadOnlyList<ExtractedRelation>> ExtractAsync(string text, CancellationToken cancellationToken = default);
}

public interface IGraphMemoryStore
{
    Task UpsertAsync(IReadOnlyList<ExtractedRelation> relations, string memoryId, CancellationToken cancellationToken = default);
    Task RemoveMemoryAsync(string memoryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, double>> GetMemoryBoostsAsync(string query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryRelation>> GetRelationsAsync(string? query = null, CancellationToken cancellationToken = default);
    Task ResetAsync(CancellationToken cancellationToken = default);
}

public interface IMemoryTelemetry
{
    Task CaptureAsync(MemoryTelemetryEvent telemetryEvent, CancellationToken cancellationToken = default);
}

public interface IMemoryService
{
    Task<AddResult> AddAsync(string text, string userId = "default_user", string? agentId = null, string? runId = null, MemoryScope scope = MemoryScope.User, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default);
    Task<AddResult> AddAsync(IEnumerable<Message> messages, string userId = "default_user", string? agentId = null, string? runId = null, MemoryScope scope = MemoryScope.User, CancellationToken cancellationToken = default);
    Task<AddResult> AddAsync(string text, MemoryAddOptions options, CancellationToken cancellationToken = default);
    Task<AddResult> AddAsync(IEnumerable<Message> messages, MemoryAddOptions options, CancellationToken cancellationToken = default);
    Task<AddResult> AddManyAsync(IEnumerable<string> texts, MemoryAddOptions? options = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, MemoryFilter? filter = null, int? topK = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, MemorySearchOptions options, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IReadOnlyList<SearchResult>>> SearchManyAsync(IEnumerable<string> queries, MemoryFilter? filter = null, int? topK = null, CancellationToken cancellationToken = default);
    Task<Memory?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Memory>> GetAllAsync(MemoryFilter? filter = null, CancellationToken cancellationToken = default);
    Task<MemoryPage> GetPageAsync(MemoryPageOptions options, MemoryFilter? filter = null, CancellationToken cancellationToken = default);
    Task<Memory> UpdateAsync(string id, string text, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default);
    Task<Memory> UpdateAsync(string id, MemoryUpdate update, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task<int> DeleteAllAsync(MemoryFilter? filter = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryHistoryEntry>> GetHistoryAsync(string id, CancellationToken cancellationToken = default);
    Task ResetAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryRelation>> GetRelationsAsync(string? query = null, CancellationToken cancellationToken = default);
}
