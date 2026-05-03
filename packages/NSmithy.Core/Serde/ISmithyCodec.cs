using System.Buffers;

namespace NSmithy.Core.Serde;

/// <summary>
/// A Smithy codec. Implementations construct format-specific
/// <see cref="IShapeSerializer"/> / <see cref="IShapeDeserializer"/> instances over a stream
/// or byte buffer; the visitor types do the actual reading and writing.
/// </summary>
/// <remarks>
/// The two factory methods are the only members implementations must provide. Convenience
/// helpers (<see cref="SmithyCodecExtensions.Serialize"/>, <see cref="SmithyCodecExtensions.Deserialize"/>)
/// are extension methods so the interface stays small and codec authors don't have to think
/// about buffer lifetime.
/// </remarks>
public interface ISmithyCodec
{
    /// <summary>The IANA media type this codec produces / consumes (e.g. <c>application/json</c>).</summary>
    string MediaType { get; }

    /// <summary>Create a serializer that writes to <paramref name="sink"/>.</summary>
    IShapeSerializer CreateSerializer(Stream sink);

    /// <summary>Create a deserializer that reads from <paramref name="source"/>.</summary>
    IShapeDeserializer CreateDeserializer(ReadOnlySequence<byte> source);
}
