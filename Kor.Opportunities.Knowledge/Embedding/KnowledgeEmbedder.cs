using System.Buffers.Binary;
using SmartComponents.LocalEmbeddings;

namespace Kor.Opportunities.Knowledge.Embedding;

public interface IKnowledgeEmbedder
{
    float[] Embed(string text);
}

/// <summary>JoeBrain's local ONNX embed call; no service, API key or remote inference.</summary>
public sealed class KnowledgeEmbedder : IKnowledgeEmbedder, IDisposable
{
    public const string ModelName = "bge-small-en-v1.5";
    public const int Dimensions = 384;
    private readonly Lazy<LocalEmbedder> _embedder = new(() => new LocalEmbedder(ModelName));

    public float[] Embed(string text)
    {
        var vector = _embedder.Value.Embed(text).Values.ToArray();
        if (vector.Length != Dimensions) throw new InvalidDataException($"Expected {Dimensions} embedding dimensions, got {vector.Length}.");
        return vector;
    }

    public static byte[] ToBytes(ReadOnlySpan<float> vector)
    {
        if (vector.Length != Dimensions) throw new ArgumentException($"Expected {Dimensions} floats.", nameof(vector));
        var bytes = new byte[Dimensions * sizeof(float)];
        for (var i = 0; i < vector.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * sizeof(float), sizeof(float)), vector[i]);
        return bytes;
    }

    public static float[] FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Dimensions * sizeof(float)) throw new ArgumentException("Expected 1536 embedding bytes.", nameof(bytes));
        var vector = new float[Dimensions];
        for (var i = 0; i < vector.Length; i++)
            vector[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(i * sizeof(float), sizeof(float)));
        return vector;
    }

    public void Dispose()
    {
        if (_embedder.IsValueCreated) _embedder.Value.Dispose();
    }
}
