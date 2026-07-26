namespace Mem0Sharp;

public enum MemoryHistoryEvent
{
    Add,
    Update,
    Delete
}

public sealed record MemoryHistoryEntry
{
    public required string Id { get; init; }
    public required string MemoryId { get; init; }
    public required MemoryHistoryEvent Event { get; init; }
    public string? OldMemory { get; init; }
    public string? NewMemory { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}