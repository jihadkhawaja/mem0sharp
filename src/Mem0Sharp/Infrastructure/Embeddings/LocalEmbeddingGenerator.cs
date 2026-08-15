using System.Numerics.Tensors;
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

        var norm = TensorPrimitives.Norm(vector);
        if (norm > 0)
        {
            TensorPrimitives.Divide(vector, norm, vector);
        }
        return Task.FromResult<IReadOnlyList<float>>(vector);
    }

    public async Task<IReadOnlyList<IReadOnlyList<float>>> GenerateBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        var vectors = new IReadOnlyList<float>[texts.Count];
        for (var index = 0; index < texts.Count; index++) vectors[index] = await GenerateAsync(texts[index], cancellationToken);
        return vectors;
    }

    private static int StableHash(ReadOnlySpan<char> value)
    {
        unchecked
        {
            var hash = 17;
            Span<byte> utf8Bytes = stackalloc byte[128];
            if (Encoding.UTF8.TryGetBytes(value, utf8Bytes, out var written))
            {
                foreach (var b in utf8Bytes[..written]) hash = hash * 31 + b;
            }
            else
            {
                var heapBytes = Encoding.UTF8.GetBytes(value.ToString());
                foreach (var b in heapBytes) hash = hash * 31 + b;
            }
            return hash & int.MaxValue;
        }
    }
}
