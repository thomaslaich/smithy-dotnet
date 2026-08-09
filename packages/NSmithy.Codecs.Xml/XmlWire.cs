using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Xml;

internal static class XmlWire
{
    private static readonly ShapeId ClientOptionalTrait = new("smithy.api", "clientOptional");
    private static readonly ShapeId DefaultTrait = new("smithy.api", "default");

    internal static bool TryCreateDefaultValue<T>(
        Schema<T> schema,
        IReadOnlyDictionary<ShapeId, Trait> traits,
        out T? value
    )
    {
        if (
            traits.ContainsKey(ClientOptionalTrait)
            || !traits.TryGetValue(DefaultTrait, out var trait)
            || trait.Value.Kind == DocumentKind.Null
        )
        {
            value = default;
            return false;
        }

        value = CreateDefaultValue(schema, trait.Value);
        return value is not null;
    }

    private static T? CreateDefaultValue<T>(Schema<T> schema, Document value)
    {
        var resolved = schema.Resolved;
        if (resolved is INullableSchema nullable)
        {
            return (T?)CreateDefaultValue((dynamic)nullable.Target, value);
        }

        return resolved.Kind switch
        {
            ShapeKind.Boolean => (T)(object)value.AsBoolean(),
            ShapeKind.Byte => (T)(object)(sbyte)value.AsNumber(),
            ShapeKind.Short => (T)(object)(short)value.AsNumber(),
            ShapeKind.Integer => (T)(object)(int)value.AsNumber(),
            ShapeKind.Long => (T)(object)(long)value.AsNumber(),
            ShapeKind.Float => (T)(object)(float)value.AsNumber(),
            ShapeKind.Double => (T)(object)(double)value.AsNumber(),
            ShapeKind.BigInteger => (T)(object)new BigInteger(value.AsNumber()),
            ShapeKind.BigDecimal => (T)(object)value.AsNumber(),
            ShapeKind.String => (T)(object)value.AsString(),
            ShapeKind.Enum => (T)((IStringEnumSchema)resolved).CreateObject(value.AsString()),
            ShapeKind.IntEnum => (T)((IIntEnumSchema)resolved).CreateObject((int)value.AsNumber()),
            ShapeKind.Blob => (T)(object)Convert.FromBase64String(value.AsString()),
            ShapeKind.Timestamp => (T)
                (object)DateTimeOffset.FromUnixTimeSeconds((long)value.AsNumber()),
            ShapeKind.List or ShapeKind.Set when resolved is IListSchema list => CreateDefaultList(
                (dynamic)list,
                value
            ),
            ShapeKind.Map when resolved is IMapSchema map => CreateDefaultMap((dynamic)map, value),
            _ => null,
        };
    }

    private static TCollection CreateDefaultList<TCollection, TElement, TBuilder>(
        IListSchema<TCollection, TElement, TBuilder> schema,
        Document value
    )
    {
        var builder = schema.CreateTypedBuilder();
        foreach (var item in value.AsArray())
        {
            schema.Add(builder, CreateDefaultValue(schema.TypedElementMember.TargetSchema, item)!);
        }

        return schema.Build(builder);
    }

    private static TDictionary CreateDefaultMap<TDictionary, TValue, TBuilder>(
        IMapSchema<TDictionary, TValue, TBuilder> schema,
        Document value
    )
    {
        var builder = schema.CreateTypedBuilder();
        foreach (var entry in value.AsObject())
        {
            schema.Add(
                builder,
                entry.Key,
                CreateDefaultValue(schema.TypedValueMember.TargetSchema, entry.Value)!
            );
        }

        return schema.Build(builder);
    }

    // Element lookups match on local name only: AWS restXml responses carry a default
    // xmlns on the root (via @xmlNamespace) that all descendants inherit, whereas the
    // schema's element names are unqualified. Namespace-sensitive XName matching would
    // miss every namespaced child (e.g. an S3 ListBuckets list coming back empty).
    internal static IEnumerable<XElement> ChildElements(XElement parent, string localName) =>
        parent
            .Elements()
            .Where(e => string.Equals(e.Name.LocalName, localName, StringComparison.Ordinal));

    internal static XElement? ChildElement(XElement parent, string localName) =>
        ChildElements(parent, localName).FirstOrDefault();

    internal static T ReadScalar<T>(
        Schema<T> schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        string value
    )
    {
        var resolved = schema.Resolved;
        if (resolved is INullableSchema nullable)
        {
            return (T)ReadScalar((dynamic)nullable.Target, traits, value);
        }

        return resolved.Kind switch
        {
            ShapeKind.Boolean => (T)
                (object)string.Equals(value, "true", StringComparison.OrdinalIgnoreCase),
            ShapeKind.Byte => (T)(object)sbyte.Parse(value, CultureInfo.InvariantCulture),
            ShapeKind.Short => (T)(object)short.Parse(value, CultureInfo.InvariantCulture),
            ShapeKind.Integer => (T)(object)int.Parse(value, CultureInfo.InvariantCulture),
            ShapeKind.Long => (T)(object)long.Parse(value, CultureInfo.InvariantCulture),
            ShapeKind.Float => (T)(object)float.Parse(value, CultureInfo.InvariantCulture),
            ShapeKind.Double => (T)(object)double.Parse(value, CultureInfo.InvariantCulture),
            ShapeKind.BigInteger => (T)
                (object)BigInteger.Parse(value, CultureInfo.InvariantCulture),
            ShapeKind.BigDecimal => (T)(object)decimal.Parse(value, CultureInfo.InvariantCulture),
            ShapeKind.String => (T)(object)value,
            ShapeKind.Enum => (T)((IStringEnumSchema)resolved).CreateObject(value),
            ShapeKind.IntEnum => (T)
                ((IIntEnumSchema)resolved).CreateObject(
                    int.Parse(value, CultureInfo.InvariantCulture)
                ),
            ShapeKind.Blob => (T)(object)Convert.FromBase64String(value),
            ShapeKind.Timestamp => (T)(object)ParseTimestamp(resolved, traits, value),
            _ => throw new InvalidOperationException(
                $"XML attribute value cannot target schema kind '{resolved.Kind}'."
            ),
        };
    }

    internal static string FormatScalar<T>(
        Schema<T> schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        T value
    )
    {
        var resolved = schema.Resolved;
        if (resolved is INullableSchema nullable)
        {
            return FormatScalar((dynamic)nullable.Target, traits, (dynamic)value!);
        }

        return resolved.Kind switch
        {
            ShapeKind.Boolean => (bool)(object)value! ? "true" : "false",
            ShapeKind.Byte => ((sbyte)(object)value!).ToString(CultureInfo.InvariantCulture),
            ShapeKind.Short => ((short)(object)value!).ToString(CultureInfo.InvariantCulture),
            ShapeKind.Integer => ((int)(object)value!).ToString(CultureInfo.InvariantCulture),
            ShapeKind.Long => ((long)(object)value!).ToString(CultureInfo.InvariantCulture),
            ShapeKind.Float => ((float)(object)value!).ToString(CultureInfo.InvariantCulture),
            ShapeKind.Double => ((double)(object)value!).ToString(CultureInfo.InvariantCulture),
            ShapeKind.BigInteger => ((BigInteger)(object)value!).ToString(
                CultureInfo.InvariantCulture
            ),
            ShapeKind.BigDecimal => ((decimal)(object)value!).ToString(
                CultureInfo.InvariantCulture
            ),
            ShapeKind.String => (string)(object)value!,
            ShapeKind.Enum => ((IStringEnumValue)(object)value!).Value,
            ShapeKind.IntEnum => ((IIntEnumSchema)resolved)
                .GetIntegerValueObject(value!)
                .ToString(CultureInfo.InvariantCulture),
            ShapeKind.Blob => Convert.ToBase64String((byte[])(object)value!),
            ShapeKind.Timestamp => FormatTimestamp(
                resolved,
                traits,
                (DateTimeOffset)(object)value!
            ),
            _ => throw new InvalidOperationException(
                $"XML scalar value cannot target schema kind '{resolved.Kind}'."
            ),
        };
    }

    internal static string FormatTimestamp(
        Schema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        DateTimeOffset value
    )
    {
        return XmlTraits.GetTimestampFormat(schema, traits) switch
        {
            "epoch-seconds" => (value.ToUnixTimeMilliseconds() / 1000.0).ToString(
                CultureInfo.InvariantCulture
            ),
            "http-date" => value.ToString("r", CultureInfo.InvariantCulture),
            _ => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        };
    }

    internal static DateTimeOffset ParseTimestamp(
        Schema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        string value
    )
    {
        return XmlTraits.GetTimestampFormat(schema, traits) switch
        {
            "epoch-seconds" => DateTimeOffset.FromUnixTimeMilliseconds(
                (long)(double.Parse(value, CultureInfo.InvariantCulture) * 1000)
            ),
            "http-date" => DateTimeOffset.ParseExact(
                value,
                "r",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None
            ),
            _ => DateTimeOffset.Parse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind
            ),
        };
    }

    internal static string ListItemName(IListSchema schema) =>
        XmlTraits.GetXmlName(schema.ElementMember) ?? schema.Element.MemberName ?? "member";

    internal static string ElementName(IMemberSchema member) =>
        XmlTraits.GetXmlName(member) ?? member.Name;

    internal static string RootElementName(Schema schema) =>
        XmlTraits.GetXmlName(schema) ?? schema.Id.Name;
}
