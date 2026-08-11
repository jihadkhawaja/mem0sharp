using System.Collections.Concurrent;

namespace Mem0Sharp;

public sealed class InMemoryStore : IBulkMemoryStore, IBatchMemoryStore, IAtomicMemoryStore, IResettableMemoryStore
{
    private readonly ConcurrentDictionary<string, Memory> memories = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<MemoryHistoryEntry>> history = new(StringComparer.Ordinal);
    private readonly object sync = new();

    public Task SaveAsync(Memory memory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync) memories[memory.Id] = memory;
        return Task.CompletedTask;
    }

    public Task SaveBatchAsync(IReadOnlyList<Memory> items, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            foreach (var memory in items) memories[memory.Id] = memory;
        }
        return Task.CompletedTask;
    }

    public Task SaveBatchWithHistoryAsync(IReadOnlyList<MemoryWriteRecord> records, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            foreach (var record in records) memories[record.Memory.Id] = record.Memory;
            foreach (var record in records) SaveHistoryCore(record.History);
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
        foreach (var memory in memories.Values.OrderByDescending(item => item.UpdatedAt))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (MemoryFilterEvaluator.Matches(memory, filter))
            {
                yield return memory;
            }
            await Task.Yield();
        }
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync) memories.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public Task DeleteWithHistoryAsync(string id, MemoryHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            memories.TryRemove(id, out _);
            SaveHistoryCore(entry);
        }
        return Task.CompletedTask;
    }

    public async Task<int> DeleteAllAsync(MemoryFilter? filter = null, CancellationToken cancellationToken = default)
    {
        var matching = new List<string>();
        await foreach (var memory in GetAllAsync(filter, cancellationToken)) matching.Add(memory.Id);
        lock (sync)
        {
            foreach (var id in matching) memories.TryRemove(id, out _);
        }
        return matching.Count;
    }

    public Task DeleteAllWithHistoryAsync(IReadOnlyList<MemoryDeleteRecord> records, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            foreach (var record in records) memories.TryRemove(record.Memory.Id, out _);
            foreach (var record in records) SaveHistoryCore(record.History);
        }
        return Task.CompletedTask;
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

    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            memories.Clear();
            history.Clear();
        }
        return Task.CompletedTask;
    }

    private void SaveHistoryCore(MemoryHistoryEntry entry) => history.GetOrAdd(entry.MemoryId, static _ => new ConcurrentQueue<MemoryHistoryEntry>()).Enqueue(entry);
}
