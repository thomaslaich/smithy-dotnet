using System.Text;
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
) : ICodec<T>
{
    private readonly XmlMemberPlan root = new XmlPlans().ForRoot(schema, memberTraits);

    public byte[] Serialize(T value)
    {
        var element = new XElement(XmlTraits.GetXmlName(memberTraits) ?? RootElementName(schema));
        ApplyNamespace(
            element,
            XmlTraits.GetXmlNamespace(schema)
                ?? (
                    defaultNamespaceUri is null
                        ? null
                        : new XmlNamespace(defaultNamespaceUri, defaultNamespacePrefix)
                )
        );
        var serializer = new XmlShapeSerializer(element, root, materializeTopLevelDefaults);
        schema.Write(MemberIndex.Root, value, ref serializer);
        return Encoding.UTF8.GetBytes(
            element.ToString(SaveOptions.DisableFormatting | SaveOptions.OmitDuplicateNamespaces)
        );
    }

    public T Deserialize(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length == 0)
        {
            return default!;
        }

        var element = XElement.Parse(
            Encoding.UTF8.GetString(payload),
            LoadOptions.PreserveWhitespace
        );
        var deserializer = new XmlShapeDeserializer(element, null, root);
        return schema.Read(ref deserializer);
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
    private readonly XmlShapePlan plan = new XmlPlans()
        .ForTarget((Schema)projection.Source)!
        .Project(name => projection.GetMember(name) is not null);

    public byte[] Serialize(T value)
    {
        var source = (Schema)projection.Source;
        var element = new XElement(
            XmlTraits.GetXmlName(source) ?? defaultRootName ?? source.Id.Name
        );
        ApplyNamespace(
            element,
            XmlTraits.GetXmlNamespace(source)
                ?? (
                    defaultNamespaceUri is null
                        ? null
                        : new XmlNamespace(defaultNamespaceUri, defaultNamespacePrefix)
                )
        );
        if (value is not null)
        {
            var serializer = new XmlShapeSerializer(element, plan, materializeTopLevelDefaults);
            projection.Source.SerializeMembers(value, ref serializer);
        }

        return Encoding.UTF8.GetBytes(
            element.ToString(SaveOptions.DisableFormatting | SaveOptions.OmitDuplicateNamespaces)
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

        var element = XElement.Parse(
            Encoding.UTF8.GetString(payload),
            LoadOptions.PreserveWhitespace
        );
        XmlShapeDeserializer.ReadMembers(element, plan, projection.Source, builder);
    }
}
