namespace Mem0Sharp;

public enum MemoryScope
{
    User,
    Session,
    Agent
}

public sealed record Memory
{
    public required string Id { get; init; }
    public required string Text { get; init; }
    public required string UserId { get; init; }
    public string? AgentId { get; init; }
    public string? RunId { get; init; }
    public MemoryScope Scope { get; init; } = MemoryScope.User;
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record MemoryInput(string Text, MemoryScope Scope = MemoryScope.User, IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record Message(string Role, string Content);

public sealed record MemoryFilter(string? UserId = null, string? AgentId = null, string? RunId = null, MemoryScope? Scope = null);

public sealed record SearchResult(Memory Memory, double Score);

public sealed record AddResult(IReadOnlyList<Memory> Memories);

public sealed record MemoryOptions
{
    public int DefaultTopK { get; init; } = 5;
    public double MinimumScore { get; init; } = 0.05;
    public int MaxCandidateCount { get; init; } = 1000;
}
