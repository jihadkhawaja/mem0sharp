namespace Mem0Sharp;

public interface IEmbeddingGenerator
{
    Task<IReadOnlyList<float>> GenerateAsync(string text, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IReadOnlyList<float>>> GenerateBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
}