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
    public DateTimeOffset? ExpiresAt { get; init; }
    public string Hash { get; init; } = string.Empty;
    public MemoryBehavior Behavior { get; init; } = MemoryBehavior.Normal;
    public string? MemoryType { get; init; }
}

public sealed record MemoryInput(string Text, MemoryScope Scope = MemoryScope.User, IReadOnlyDictionary<string, string>? Metadata = null, DateTimeOffset? ExpiresAt = null, MemoryBehavior Behavior = MemoryBehavior.Normal, string? MemoryType = null);

public sealed record Message(string Role, string Content);