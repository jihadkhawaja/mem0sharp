namespace Mem0Sharp;

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
    public MemoryBehavior? Behavior { get; init; }
    public bool IncludeNonFactual { get; init; }
    public double RecencyBias { get; init; }
    public TimeSpan? FreshnessWindow { get; init; }
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