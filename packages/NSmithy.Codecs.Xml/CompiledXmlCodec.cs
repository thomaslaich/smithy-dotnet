using System.Numerics;
using System.Xml.Linq;
using NSmithy.Core;
using NSmithy.Core.Serde;
using static NSmithy.Codecs.Xml.XmlWire;

namespace NSmithy.Codecs.Xml;

internal sealed class CompiledXmlCodec<T>(
    Schema<T> schema,
    bool materializeTopLevelDefaults,
    IReadOnlyDictionary<ShapeId, Trait>? memberTraits = null,
    string? defaultNamespaceUri = null,
    string? defaultNamespacePrefix = null
) : IXmlCodec<T>
{
    private readonly IXmlValueWriter<T> valueWriter = XmlWriterCompiler.Compile(
        schema,
        materializeTopLevelDefaults
    );
    private readonly IXmlValueReader<T> valueReader = XmlReaderCompiler.Compile(schema);

    public byte[] Serialize(T value)
    {
        var root = new XElement(XmlTraits.GetXmlName(memberTraits) ?? RootElementName(schema));
        ApplyNamespace(
            root,
            XmlTraits.GetXmlNamespace(schema)
                ?? (
                    defaultNamespaceUri is null
                        ? null
                        : new XmlNamespace(defaultNamespaceUri, defaultNamespacePrefix)
                )
        );
        valueWriter.Write(root, value);
        return System.Text.Encoding.UTF8.GetBytes(
            root.ToString(SaveOptions.DisableFormatting | SaveOptions.OmitDuplicateNamespaces)
        );
    }

    public T Deserialize(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length == 0)
        {
            return default!;
        }

        var root = XElement.Parse(
            System.Text.Encoding.UTF8.GetString(payload),
            LoadOptions.PreserveWhitespace
        );
        return valueReader.Read(root);
    }
}

internal sealed class CompiledXmlProjectionCodec<T, TBuilder>(
    StructProjection<T, TBuilder> projection,
    bool materializeTopLevelDefaults,
    string? defaultRootName,
    string? defaultNamespaceUri,
    string? defaultNamespacePrefix
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
        var source = (Schema)projection.Source;
        var root = new XElement(XmlTraits.GetXmlName(source) ?? defaultRootName ?? source.Id.Name);
        ApplyNamespace(
            root,
            XmlTraits.GetXmlNamespace(source)
                ?? (
                    defaultNamespaceUri is null
                        ? null
                        : new XmlNamespace(defaultNamespaceUri, defaultNamespacePrefix)
                )
        );
        valueWriter.Write(root, value);
        return System.Text.Encoding.UTF8.GetBytes(
            root.ToString(SaveOptions.DisableFormatting | SaveOptions.OmitDuplicateNamespaces)
        );
    }

    public void ReadInto(byte[] payload, TBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(builder);
        if (payload.Length == 0)
        {
            return;
        }

        var root = XElement.Parse(
            System.Text.Encoding.UTF8.GetString(payload),
            LoadOptions.PreserveWhitespace
        );
        valueReader.ReadInto(builder, root);
    }
}
