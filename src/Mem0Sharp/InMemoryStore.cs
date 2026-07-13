using System.Collections.Concurrent;

namespace Mem0Sharp;

public sealed class InMemoryStore : IBulkMemoryStore
{
    private readonly ConcurrentDictionary<string, Memory> memories = new(StringComparer.Ordinal);

    public Task SaveAsync(Memory memory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        memories[memory.Id] = memory;
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
            if (Matches(memory, filter))
            {
                yield return memory;
            }
            await Task.Yield();
        }
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        memories.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public async Task<int> DeleteAllAsync(MemoryFilter? filter = null, CancellationToken cancellationToken = default)
    {
        var matching = new List<string>();
        await foreach (var memory in GetAllAsync(filter, cancellationToken)) matching.Add(memory.Id);
        foreach (var id in matching) memories.TryRemove(id, out _);
        return matching.Count;
    }

    private static bool Matches(Memory memory, MemoryFilter? filter) =>
        filter is null ||
        (filter.UserId is null || memory.UserId == filter.UserId) &&
        (filter.AgentId is null || memory.AgentId == filter.AgentId) &&
        (filter.RunId is null || memory.RunId == filter.RunId) &&
        (filter.Scope is null || memory.Scope == filter.Scope);
}
