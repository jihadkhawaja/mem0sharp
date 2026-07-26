namespace Mem0Sharp;

public sealed record MemoryServiceConfiguration
{
    public IMemoryStore? Store { get; init; }
    public IEmbeddingGenerator? Embeddings { get; init; }
    public IMemoryExtractor? Extractor { get; init; }
    public MemoryOptions? Options { get; init; }
    public IMemoryReranker? Reranker { get; init; }
    public IMemoryConflictResolver? ConflictResolver { get; init; }
    public IProceduralMemoryGenerator? ProceduralMemoryGenerator { get; init; }
    public IEntityExtractor? EntityExtractor { get; init; }
    public IEntityStore? EntityStore { get; init; }
    public IGraphMemoryExtractor? GraphExtractor { get; init; }
    public IGraphMemoryStore? GraphStore { get; init; }
    public IMemoryTelemetry? Telemetry { get; init; }

    public IMemoryService CreateService()
    {
        IMemoryService service = new MemoryService(Store, Embeddings, Extractor, Options, Reranker, ConflictResolver, ProceduralMemoryGenerator, EntityExtractor, EntityStore, GraphExtractor, GraphStore);
        return Telemetry is null ? service : new TelemetryMemoryService(service, Telemetry);
    }
}