using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Xml;

internal static class XmlTraits
{
    private static readonly ShapeId XmlNameId = ShapeId.Parse("smithy.api#xmlName");
    private static readonly ShapeId XmlAttributeId = ShapeId.Parse("smithy.api#xmlAttribute");
    private static readonly ShapeId XmlFlattenedId = ShapeId.Parse("smithy.api#xmlFlattened");
    private static readonly ShapeId XmlNamespaceId = ShapeId.Parse("smithy.api#xmlNamespace");
    private static readonly ShapeId TimestampFormatId = ShapeId.Parse("smithy.api#timestampFormat");

    public static string? GetXmlName(Schema schema) =>
        schema.GetTrait(XmlNameId) is { HasValue: true } t ? t.Value.AsString() : null;

    public static string? GetXmlName(IReadOnlyDictionary<ShapeId, Trait>? traits) =>
        traits is not null && traits.TryGetValue(XmlNameId, out var trait) && trait.HasValue
            ? trait.Value.AsString()
            : null;

    public static string? GetXmlName(IMemberSchema schema) =>
        schema.MemberTraits.TryGetValue(XmlNameId, out var trait) && trait.HasValue
            ? trait.Value.AsString()
            : null;

    public static bool IsXmlAttribute(IMemberSchema schema) =>
        schema.MemberTraits.ContainsKey(XmlAttributeId);

    public static bool IsXmlFlattened(IMemberSchema schema) =>
        schema.MemberTraits.ContainsKey(XmlFlattenedId);

    public static XmlNamespace? GetXmlNamespace(Schema schema) =>
        ReadXmlNamespace(schema.GetTrait(XmlNamespaceId));

    public static XmlNamespace? GetXmlNamespace(
        Schema schema,
        IReadOnlyDictionary<ShapeId, Trait> memberTraits
    ) =>
        memberTraits.TryGetValue(XmlNamespaceId, out var memberTrait)
            ? ReadXmlNamespace(memberTrait)
            : GetXmlNamespace(schema);

    private static XmlNamespace? ReadXmlNamespace(Trait? trait)
    {
        if (trait is not { HasValue: true })
            return null;

        var value = trait.GetValueOrDefault().Value.AsObject();
        var uri = value["uri"].AsString();
        var prefix = value.TryGetValue("prefix", out var prefixValue)
            ? prefixValue.AsString()
            : null;
        return new XmlNamespace(uri, prefix);
    }

    public static string? GetTimestampFormat(
        Schema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits
    )
    {
        if (
            traits is not null
            && traits.TryGetValue(TimestampFormatId, out var memberTrait)
            && memberTrait.HasValue
        )
        {
            return memberTrait.Value.AsString();
        }

        return schema.GetTrait(TimestampFormatId) is { HasValue: true } trait
            ? trait.Value.AsString()
            : null;
    }
}

internal readonly record struct XmlNamespace(string Uri, string? Prefix);
