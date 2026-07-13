using System.Numerics;
using System.Text;

namespace Mem0Sharp;

public sealed class LocalEmbeddingGenerator : IEmbeddingGenerator
{
    public int Dimensions { get; }

    public LocalEmbeddingGenerator(int dimensions = 384)
    {
        if (dimensions < 8) throw new ArgumentOutOfRangeException(nameof(dimensions));
        Dimensions = dimensions;
    }

    public Task<IReadOnlyList<float>> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var vector = new float[Dimensions];
        var tokens = text.ToLowerInvariant().Split([' ', '\t', '\r', '\n', '.', ',', '!', '?', ';', ':'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            var hash = StableHash(token);
            vector[(uint)hash % (uint)Dimensions] += 1f;
            vector[(uint)(hash >> 16) % (uint)Dimensions] += 0.5f;
        }

        var norm = MathF.Sqrt(vector.Sum(value => value * value));
        if (norm > 0)
        {
            for (var index = 0; index < vector.Length; index++) vector[index] /= norm;
        }
        return Task.FromResult<IReadOnlyList<float>>(vector);
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 17;
            foreach (var character in Encoding.UTF8.GetBytes(value)) hash = hash * 31 + character;
            return hash & int.MaxValue;
        }
    }
}
