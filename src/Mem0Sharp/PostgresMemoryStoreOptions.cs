namespace Mem0Sharp;

public sealed record PostgresMemoryStoreOptions
{
    public required string ConnectionString { get; init; }
    public string TableName { get; init; } = "mem0_memories";
    public required int EmbeddingDimensions { get; init; }
    public bool UseHnswIndex { get; init; } = true;
    public bool CreateExtension { get; init; } = true;
}