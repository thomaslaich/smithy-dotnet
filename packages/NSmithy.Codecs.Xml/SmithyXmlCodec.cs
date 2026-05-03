using System.Buffers;
using System.Xml.Linq;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Xml;

/// <summary>
/// XML codec that walks shapes via the <see cref="IShapeSerializer"/> /
/// <see cref="IShapeDeserializer"/> visitor interfaces. No reflection; no annotation-based type
/// resolution.
/// </summary>
public sealed class SmithyXmlCodec : ISmithyCodec
{
    public static SmithyXmlCodec Default { get; } = new();

    public string MediaType => "application/xml";

    public IShapeSerializer CreateSerializer(Stream sink) => new XmlShapeSerializer(sink);

    public IShapeDeserializer CreateDeserializer(ReadOnlySequence<byte> source)
    {
        var bytes = source.ToArray();
        if (bytes.Length == 0)
        {
            return new XmlShapeDeserializer(null);
        }

        var doc = XDocument.Load(new MemoryStream(bytes));
        return new XmlShapeDeserializer(doc.Root);
    }
}
