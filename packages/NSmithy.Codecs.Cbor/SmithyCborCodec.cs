using System.Buffers;
using System.Formats.Cbor;
using System.Globalization;
using System.Numerics;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Cbor;

/// <summary>
/// CBOR codec that walks shapes via the <see cref="IShapeSerializer"/> /
/// <see cref="IShapeDeserializer"/> visitor interfaces. Follows the same pattern as
/// <c>SmithyJsonCodec</c>. No reflection; no annotation-based type resolution.
/// </summary>
public sealed class SmithyCborCodec : ISmithyCodec
{
    public static SmithyCborCodec Default { get; } = new();

    public string MediaType => "application/cbor";

    public IShapeSerializer CreateSerializer(Stream sink) => new CborShapeSerializer(sink);

    public IShapeDeserializer CreateDeserializer(ReadOnlySequence<byte> source) =>
        CborShapeDeserializer.Parse(source.ToArray());
}
