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
}

public sealed record MemoryInput(string Text, MemoryScope Scope = MemoryScope.User, IReadOnlyDictionary<string, string>? Metadata = null, DateTimeOffset? ExpiresAt = null);

public sealed record Message(string Role, string Content);

public sealed record MemoryFilter(
    string? UserId = null,
    string? AgentId = null,
    string? RunId = null,
    MemoryScope? Scope = null,
    FilterExpression? Metadata = null,
    bool IncludeExpired = false);

public enum FilterOperator
{
    Equal,
    NotEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    In,
    NotIn,
    Contains,
    ContainsIgnoreCase,
    Exists
}

public enum FilterLogic
{
    And,
    Or,
    Not
}

public abstract record FilterExpression;

public sealed record MetadataFilter(string Key, FilterOperator Operator, object? Value = null) : FilterExpression;

public sealed record FilterGroup(FilterLogic Logic, IReadOnlyList<FilterExpression> Filters) : FilterExpression
{
    public FilterGroup(FilterLogic logic, params FilterExpression[] filters) : this(logic, (IReadOnlyList<FilterExpression>)filters) { }
}

public sealed record MemoryAddOptions
{
    public string UserId { get; init; } = "default_user";
    public string? AgentId { get; init; }
    public string? RunId { get; init; }
    public MemoryScope Scope { get; init; } = MemoryScope.User;
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public bool Infer { get; init; } = true;
    public string? Prompt { get; init; }
    public string? MemoryType { get; init; }
    public bool Deduplicate { get; init; } = true;
}

public sealed record MemoryUpdate
{
    public string? Text { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
    public bool UpdateExpiration { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed record MemoryPageOptions
{
    public int Offset { get; init; }
    public int Limit { get; init; } = 20;
}

public sealed record MemoryPage(IReadOnlyList<Memory> Results, int Total, int Offset, int Limit);

public sealed record MemorySearchOptions
{
    public MemoryFilter? Filter { get; init; }
    public int TopK { get; init; } = 20;
    public double Threshold { get; init; } = 0.1;
    public bool Rerank { get; init; }
    public bool Explain { get; init; }
    public bool Hybrid { get; init; } = true;
}

public sealed record SearchScoreDetails(
    double Semantic,
    double Keyword = 0,
    double Entity = 0,
    double? Reranker = null,
    double Raw = 0,
    double MaxPossible = 1,
    double Threshold = 0);

public sealed record SearchResult(Memory Memory, double Score, SearchScoreDetails? ScoreDetails = null);

public enum MemoryAction
{
    Add,
    Update,
    Delete,
    None
}

public sealed record MemoryDecision(string Text, MemoryAction Event, string? MemoryId = null, IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record MemoryActionResult(string? Id, string? Memory, MemoryAction Event);

public sealed record AddResult(IReadOnlyList<Memory> Memories, IReadOnlyList<MemoryActionResult>? Actions = null);

public enum EntityType
{
    Person,
    Organization,
    Location,
    Concept,
    Other
}

public sealed record ExtractedEntity(string Text, EntityType Type = EntityType.Other);

public sealed record MemoryEntity(string Id, string Text, EntityType Type, IReadOnlySet<string> LinkedMemoryIds);

public sealed record ExtractedRelation(string Source, string Relationship, string Target);

public sealed record MemoryRelation(string Id, string Source, string Relationship, string Target, string MemoryId);

public sealed record MemoryVectorRecord(Memory Memory, IReadOnlyList<float> Embedding);

public sealed record MemoryTelemetryEvent(string Name, DateTimeOffset Timestamp, IReadOnlyDictionary<string, object?> Properties);

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

public sealed record MemoryOptions
{
    public int DefaultTopK { get; init; } = 5;
    public double MinimumScore { get; init; } = 0.05;
    public int MaxCandidateCount { get; init; } = 1000;
    public bool EnableHybridSearch { get; init; } = true;
}
