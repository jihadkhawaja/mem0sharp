using System.Collections.Concurrent;
using System.Numerics.Tensors;

namespace Mem0Sharp;

public sealed class InMemoryStore : IMemoryStore
{
    private readonly ConcurrentDictionary<string, Memory> memories = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, float[]> vectors = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<MemoryHistoryEntry>> history = new(StringComparer.Ordinal);
    private readonly object sync = new();

    public Task SaveAsync(Memory memory, IReadOnlyList<float>? embedding = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            memories[memory.Id] = memory;
            if (embedding is not null) vectors[memory.Id] = embedding.ToArray();
        }
        return Task.CompletedTask;
    }

    public Task SaveBatchAsync(IReadOnlyList<MemoryWriteRecord> records, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            foreach (var record in records)
            {
                memories[record.Memory.Id] = record.Memory;
                if (record.Embedding is not null) vectors[record.Memory.Id] = record.Embedding.ToArray();
                SaveHistoryCore(record.History);
            }
        }
        return Task.CompletedTask;
    }

    public Task<Memory?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        memories.TryGetValue(id, out var memory);
        return Task.FromResult(memory);
    }

    public async IAsyncEnumerable<Memory> GetAllAsync(MemoryFilter? filter = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var list = memories.Values.OrderByDescending(item => item.UpdatedAt).ToArray();
        foreach (var memory in list)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (MemoryFilterEvaluator.Matches(memory, filter))
            {
                yield return memory;
            }
        }
    }

    public Task<IReadOnlyList<SearchResult>> SearchAsync(IReadOnlyList<float> embedding, MemoryFilter? filter = null, int topK = 5, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (topK < 0) throw new ArgumentOutOfRangeException(nameof(topK));
        if (topK == 0) return Task.FromResult<IReadOnlyList<SearchResult>>([]);

        var queryArr = embedding.ToArray();
        var candidates = new List<SearchResult>();

        foreach (var pair in memories)
        {
            if (!MemoryFilterEvaluator.Matches(pair.Value, filter)) continue;
            var score = 0.0;
            if (vectors.TryGetValue(pair.Key, out var vector) && vector.Length == queryArr.Length)
            {
                score = (double)TensorPrimitives.CosineSimilarity(queryArr, vector);
            }
            candidates.Add(new SearchResult(pair.Value, score));
        }

        IReadOnlyList<SearchResult> results = candidates
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.Memory.UpdatedAt)
            .Take(topK)
            .ToArray();

        return Task.FromResult(results);
    }

    public Task DeleteAsync(string id, MemoryHistoryEntry? entry = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            memories.TryRemove(id, out _);
            vectors.TryRemove(id, out _);
            if (entry is not null) SaveHistoryCore(entry);
        }
        return Task.CompletedTask;
    }

    public Task<int> DeleteAllAsync(MemoryFilter? filter = null, IReadOnlyList<MemoryDeleteRecord>? records = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            if (records is not null)
            {
                foreach (var record in records)
                {
                    memories.TryRemove(record.Memory.Id, out _);
                    vectors.TryRemove(record.Memory.Id, out _);
                    SaveHistoryCore(record.History);
                }
                return Task.FromResult(records.Count);
            }

            var matching = memories.Values.Where(m => MemoryFilterEvaluator.Matches(m, filter)).Select(m => m.Id).ToArray();
            foreach (var id in matching)
            {
                memories.TryRemove(id, out _);
                vectors.TryRemove(id, out _);
            }
            return Task.FromResult(matching.Length);
        }
    }

    public Task SaveHistoryAsync(MemoryHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync) SaveHistoryCore(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MemoryHistoryEntry>> GetHistoryAsync(string memoryId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<MemoryHistoryEntry> entries = history.TryGetValue(memoryId, out var values) ? values.ToArray() : [];
        return Task.FromResult(entries);
    }

    public Task<IReadOnlyList<MemoryHistoryEntry>> GetAllHistoryAsync(MemoryFilter? filter = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var all = history.Values.SelectMany(q => q).OrderBy(h => h.UpdatedAt).ToArray();
        return Task.FromResult<IReadOnlyList<MemoryHistoryEntry>>(all);
    }

    public Task<RollbackResult> RollbackAsync(DateTimeOffset pointInTime, MemoryFilter? filter = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            var restored = 0;
            var deleted = 0;
            var affected = new HashSet<string>(StringComparer.Ordinal);

            // Reconstruct state for all memory IDs in history
            foreach (var pair in history)
            {
                var memoryId = pair.Key;
                var entriesBefore = pair.Value
                    .Where(e => e.UpdatedAt <= pointInTime)
                    .OrderBy(e => e.UpdatedAt)
                    .ToArray();

                if (entriesBefore.Length == 0)
                {
                    // Memory was created after pointInTime -> remove if present
                    if (memories.TryRemove(memoryId, out _))
                    {
                        vectors.TryRemove(memoryId, out _);
                        deleted++;
                        affected.Add(memoryId);
                    }
                }
                else
                {
                    var lastEntry = entriesBefore[^1];
                    if (lastEntry.IsDeleted || lastEntry.Event == MemoryHistoryEvent.Delete || string.IsNullOrEmpty(lastEntry.NewMemory))
                    {
                        // Was deleted at pointInTime
                        if (memories.TryRemove(memoryId, out _))
                        {
                            vectors.TryRemove(memoryId, out _);
                            deleted++;
                            affected.Add(memoryId);
                        }
                    }
                    else
                    {
                        // Active at pointInTime
                        if (memories.TryGetValue(memoryId, out var current))
                        {
                            if (current.Text != lastEntry.NewMemory)
                            {
                                memories[memoryId] = current with { Text = lastEntry.NewMemory, UpdatedAt = lastEntry.UpdatedAt };
                                restored++;
                                affected.Add(memoryId);
                            }
                        }
                        else
                        {
                            // Recreate
                            memories[memoryId] = new Memory
                            {
                                Id = memoryId,
                                Text = lastEntry.NewMemory,
                                UserId = filter?.UserId ?? "default_user",
                                AgentId = filter?.AgentId,
                                RunId = filter?.RunId,
                                CreatedAt = lastEntry.CreatedAt,
                                UpdatedAt = lastEntry.UpdatedAt
                            };
                            restored++;
                            affected.Add(memoryId);
                        }
                    }
                }
            }

            return Task.FromResult(new RollbackResult(restored, deleted, affected.ToArray()));
        }
    }

    public Task<RollbackResult> RollbackToHistoryAsync(string historyEntryId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            foreach (var pair in history)
            {
                var target = pair.Value.FirstOrDefault(e => e.Id == historyEntryId);
                if (target is not null)
                {
                    return RollbackAsync(target.UpdatedAt, cancellationToken: cancellationToken);
                }
            }
            return Task.FromResult(new RollbackResult(0, 0, []));
        }
    }

    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            memories.Clear();
            vectors.Clear();
            history.Clear();
        }
        return Task.CompletedTask;
    }

    private void SaveHistoryCore(MemoryHistoryEntry entry) => history.GetOrAdd(entry.MemoryId, static _ => new ConcurrentQueue<MemoryHistoryEntry>()).Enqueue(entry);
}
