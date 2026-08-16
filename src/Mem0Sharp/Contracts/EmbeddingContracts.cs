using Microsoft.Extensions.AI;

namespace Mem0Sharp;

public interface IEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    Task<IReadOnlyList<float>> GenerateVectorAsync(string text, CancellationToken cancellationToken = default) =>
        this.GenerateVectorCoreAsync(text, cancellationToken);

    Task<IReadOnlyList<IReadOnlyList<float>>> GenerateVectorBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
        this.GenerateVectorBatchCoreAsync(texts, cancellationToken);
}

public static class EmbeddingGeneratorExtensions
{
    public static async Task<IReadOnlyList<float>> GenerateVectorCoreAsync(
        this IEmbeddingGenerator<string, Embedding<float>> generator,
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(generator);
        var result = await generator.GenerateAsync([text], cancellationToken: cancellationToken);
        return result.Count == 0 ? [] : result[0].Vector.ToArray();
    }

    public static async Task<IReadOnlyList<IReadOnlyList<float>>> GenerateVectorBatchCoreAsync(
        this IEmbeddingGenerator<string, Embedding<float>> generator,
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Count == 0) return [];
        var result = await generator.GenerateAsync(texts, cancellationToken: cancellationToken);
        var list = new IReadOnlyList<float>[result.Count];
        for (var i = 0; i < result.Count; i++)
        {
            list[i] = result[i].Vector.ToArray();
        }
        return list;
    }
}