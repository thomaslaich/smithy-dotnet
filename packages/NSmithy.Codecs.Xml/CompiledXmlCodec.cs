using System.Numerics;
using System.Xml.Linq;
using NSmithy.Core;
using NSmithy.Core.Serde;
using static NSmithy.Codecs.Xml.XmlWire;

namespace NSmithy.Codecs.Xml;

internal sealed class CompiledXmlCodec<T>(Schema<T> schema, bool materializeTopLevelDefaults)
    : IXmlCodec<T>
{
    private readonly IXmlValueWriter<T> valueWriter = XmlWriterCompiler.Compile(
        schema,
        materializeTopLevelDefaults
    );
    private readonly IXmlValueReader<T> valueReader = XmlReaderCompiler.Compile(schema);

    public byte[] Serialize(T value)
    {
        var root = new XElement(RootElementName(schema));
        valueWriter.Write(root, value);
        return System.Text.Encoding.UTF8.GetBytes(root.ToString(SaveOptions.DisableFormatting));
    }

    public T Deserialize(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length == 0)
        {
            return default!;
        }

        var root = XElement.Parse(System.Text.Encoding.UTF8.GetString(payload));
        return valueReader.Read(root);
    }
}

internal sealed class CompiledXmlProjectionCodec<T, TBuilder>(
    StructProjection<T, TBuilder> projection,
    bool materializeTopLevelDefaults
) : IProjectionCodec<T, TBuilder>
{
    private readonly IXmlValueWriter<T> valueWriter = XmlWriterCompiler.Compile(
        projection,
        materializeTopLevelDefaults
    );
    private readonly StructureXmlProjectionReader<TBuilder> valueReader = XmlReaderCompiler.Compile(
        projection
    );

    public byte[] Serialize(T value)
    {
        var root = new XElement(RootElementName((Schema)projection.Source));
        valueWriter.Write(root, value);
        return System.Text.Encoding.UTF8.GetBytes(root.ToString(SaveOptions.DisableFormatting));
    }

    public void ReadInto(byte[] payload, TBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(builder);
        if (payload.Length == 0)
        {
            return;
        }

        var root = XElement.Parse(System.Text.Encoding.UTF8.GetString(payload));
        valueReader.ReadInto(builder, root);
    }
}
