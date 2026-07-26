namespace Mem0Sharp;

public interface IEmbeddingGenerator
{
    Task<IReadOnlyList<float>> GenerateAsync(string text, CancellationToken cancellationToken = default);
}

public interface IBatchEmbeddingGenerator : IEmbeddingGenerator
{
    Task<IReadOnlyList<IReadOnlyList<float>>> GenerateBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
}