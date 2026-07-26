namespace Mem0Sharp;

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