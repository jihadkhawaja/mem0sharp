using System.Collections.Concurrent;

namespace Mem0Sharp;

public sealed class InMemoryEntityStore : IEntityStore
{
    private readonly ConcurrentDictionary<string, EntityState> entities = new(StringComparer.OrdinalIgnoreCase);

    public Task UpsertLinksAsync(IReadOnlyList<ExtractedEntity> extractedEntities, string memoryId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var extracted in extractedEntities)
        {
            var key = Normalize(extracted.Text);
            entities.AddOrUpdate(
                key,
                _ => new EntityState(Guid.NewGuid().ToString("N"), extracted.Text.Trim(), extracted.Type, new HashSet<string>(StringComparer.Ordinal) { memoryId }),
                (_, current) => current with { LinkedMemoryIds = current.LinkedMemoryIds.Append(memoryId).ToHashSet(StringComparer.Ordinal) });
        }
        return Task.CompletedTask;
    }

    public Task RemoveMemoryAsync(string memoryId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var pair in entities)
        {
            var links = pair.Value.LinkedMemoryIds.Where(id => id != memoryId).ToHashSet(StringComparer.Ordinal);
            if (links.Count == 0) entities.TryRemove(pair.Key, out _);
            else entities[pair.Key] = pair.Value with { LinkedMemoryIds = links };
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, double>> GetMemoryBoostsAsync(IReadOnlyList<ExtractedEntity> extractedEntities, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var boosts = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var extracted in extractedEntities)
        {
            if (!entities.TryGetValue(Normalize(extracted.Text), out var entity)) continue;
            var contribution = 0.5 / Math.Max(entity.LinkedMemoryIds.Count, 1);
            foreach (var memoryId in entity.LinkedMemoryIds) boosts[memoryId] = Math.Min(0.5, boosts.GetValueOrDefault(memoryId) + contribution);
        }
        return Task.FromResult<IReadOnlyDictionary<string, double>>(boosts);
    }

    public Task<IReadOnlyList<MemoryEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<MemoryEntity> result = entities.Values
            .Select(entity => new MemoryEntity(entity.Id, entity.Text, entity.Type, entity.LinkedMemoryIds))
            .OrderBy(entity => entity.Text, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult(result);
    }

    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        entities.Clear();
        return Task.CompletedTask;
    }

    private static string Normalize(string text) => string.Join(' ', text.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private sealed record EntityState(string Id, string Text, EntityType Type, IReadOnlySet<string> LinkedMemoryIds);
}