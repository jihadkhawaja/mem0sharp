namespace Mem0Sharp.Evaluation;

/// <summary>
/// One configuration of Mem0Sharp under test. Every scenario uses PostgreSQL/pgvector
/// for storage; the scenario varies extraction behavior and search options.
/// </summary>
internal sealed record ScenarioDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public MemoryBehavior Behavior { get; init; } = MemoryBehavior.Normal;
    public string? BehaviorPersona { get; init; }
    public bool Infer { get; init; } = true;
    public bool Deduplicate { get; init; } = true;
    public bool UseConflictResolver { get; init; }
    public bool Hybrid { get; init; } = true;
    public bool Rerank { get; init; }
    public double Threshold { get; init; } = 0.1;

    /// <summary>PostgreSQL-safe table suffix for this scenario.</summary>
    internal string TableName => "eval_" + Name.Replace('-', '_');
}

internal static class Scenarios
{
    internal static IReadOnlyList<ScenarioDefinition> All { get; } =
    [
        new ScenarioDefinition
        {
            Name = "baseline",
            Description = "Default pipeline: LLM extraction, hybrid search, deduplication on, no conflict resolution, no reranking."
        },
        new ScenarioDefinition
        {
            Name = "no-hybrid",
            Description = "Semantic vector search only (hybrid keyword fusion disabled).",
            Hybrid = false
        },
        new ScenarioDefinition
        {
            Name = "llm-rerank",
            Description = "Baseline plus LlmReranker over the retrieved candidates.",
            Rerank = true
        },
        new ScenarioDefinition
        {
            Name = "conflict-resolution",
            Description = "Baseline plus LlmMemoryConflictResolver making ADD/UPDATE/DELETE/NONE decisions at add time.",
            UseConflictResolver = true
        },
        new ScenarioDefinition
        {
            Name = "no-dedup",
            Description = "Baseline with scope-aware content-hash deduplication disabled.",
            Deduplicate = false
        },
        new ScenarioDefinition
        {
            Name = "infer-off",
            Description = "Raw message storage (Infer = false); no LLM extraction at add time.",
            Infer = false
        },
        new ScenarioDefinition
        {
            Name = "strict-threshold",
            Description = "Baseline with the search score threshold raised from 0.1 to 0.3.",
            Threshold = 0.3
        },
        new ScenarioDefinition
        {
            Name = "behavior-dreaming",
            Description = "Dreaming behavior: dream-like consolidation extracting themes and associations.",
            Behavior = MemoryBehavior.Dreaming
        },
        new ScenarioDefinition
        {
            Name = "behavior-random-thoughts",
            Description = "Random-thoughts behavior: spontaneous associated thoughts with explicit uncertainty.",
            Behavior = MemoryBehavior.RandomThoughts
        },
        new ScenarioDefinition
        {
            Name = "behavior-personal-memory",
            Description = "Personal-memory behavior: first-person memories shaped by an agent persona.",
            Behavior = MemoryBehavior.PersonalMemory,
            BehaviorPersona = "You are Fern, a warm and observant companion who notices practical details and emotional meaning."
        }
    ];
}
