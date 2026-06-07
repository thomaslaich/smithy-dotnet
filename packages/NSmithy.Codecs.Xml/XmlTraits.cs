using NSmithy.Core;
using NSmithy.Core.Functional;

namespace NSmithy.Codecs.Xml;

internal static class XmlTraits
{
    private static readonly ShapeId XmlNameId = ShapeId.Parse("smithy.api#xmlName");
    private static readonly ShapeId XmlAttributeId = ShapeId.Parse("smithy.api#xmlAttribute");
    private static readonly ShapeId XmlFlattenedId = ShapeId.Parse("smithy.api#xmlFlattened");
    private static readonly ShapeId TimestampFormatId = ShapeId.Parse("smithy.api#timestampFormat");

    public static string? GetXmlName(Schema schema) =>
        schema.GetTrait(XmlNameId) is { HasValue: true } t ? t.Value.AsString() : null;

    public static string? GetXmlName(FunctionalSchema schema) =>
        schema.GetTrait(XmlNameId) is { HasValue: true } t ? t.Value.AsString() : null;

    public static string? GetXmlName(IFunctionalMemberSchema schema) =>
        schema.Traits.TryGetValue(XmlNameId, out var trait) && trait.HasValue
            ? trait.Value.AsString()
            : null;

    public static bool IsXmlAttribute(Schema schema) => schema.HasTrait(XmlAttributeId);

    public static bool IsXmlAttribute(IFunctionalMemberSchema schema) =>
        schema.Traits.ContainsKey(XmlAttributeId);

    public static bool IsXmlFlattened(Schema schema) => schema.HasTrait(XmlFlattenedId);

    public static bool IsXmlFlattened(IFunctionalMemberSchema schema) =>
        schema.Traits.ContainsKey(XmlFlattenedId);

    public static string? GetTimestampFormat(Schema schema) =>
        schema.GetTrait(TimestampFormatId) is { HasValue: true } t ? t.Value.AsString() : null;

    public static string? GetTimestampFormat(
        FunctionalSchema schema,
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

    public static string ElementName(Schema memberSchema) =>
        GetXmlName(memberSchema) ?? memberSchema.MemberName!;

    public static string RootElementName(Schema schema) => GetXmlName(schema) ?? schema.Id.Name;
}
