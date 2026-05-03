using System.Buffers;
using System.Text.Json;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Json;

/// <summary>
/// JSON codec that walks shapes via the <see cref="IShapeSerializer"/> /
/// <see cref="IShapeDeserializer"/> visitor interfaces.
/// </summary>
public sealed class SmithyJsonCodec : ISmithyCodec
{
    public static SmithyJsonCodec Default { get; } = new();

    public string MediaType => "application/json";

    public IShapeSerializer CreateSerializer(Stream sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        return new JsonShapeSerializer(sink);
    }

    public IShapeDeserializer CreateDeserializer(ReadOnlySequence<byte> source)
    {
        // Parse once into a DOM; the deserializer walks it. Using ReadOnlySequence avoids
        // copying when callers stream from a PipeReader.
        var reader = new Utf8JsonReader(source);
        var document = JsonDocument.ParseValue(ref reader);
        return new JsonShapeDeserializer(document, document.RootElement);
    }
}
