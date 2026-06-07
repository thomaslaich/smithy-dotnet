using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using NSmithy.Core;
using NSmithy.Core.Functional;

namespace NSmithy.Codecs.Xml;

public interface IFunctionalXmlCodec<T> : IFunctionalCodec<T, string> { }

public static class FunctionalXmlCodec
{
    public static IFunctionalXmlCodec<T> FromSchema<T>(FunctionalSchema<T> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new FunctionalXmlCodecImpl<T>(schema);
    }

    private sealed class FunctionalXmlCodecImpl<T>(FunctionalSchema<T> schema)
        : IFunctionalXmlCodec<T>
    {
        public string Serialize(T value)
        {
            var element = WriteRoot(schema, value);
            return element.ToString(SaveOptions.DisableFormatting);
        }

        public T Deserialize(string payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            if (payload.Length == 0)
            {
                return default!;
            }

            var root = XElement.Parse(payload);
            return (T)ReadValue(schema, root)!;
        }
    }

    private static XElement WriteRoot(FunctionalSchema schema, object? value)
    {
        var root = new XElement(RootElementName(schema));
        WriteElementValue(root, schema, value);
        return root;
    }

    private static void WriteElementValue(XElement element, FunctionalSchema schema, object? value)
    {
        var resolved = schema.Resolved;
        if (value is null)
        {
            return;
        }

        if (resolved is IFunctionalNullableSchema nullable)
        {
            WriteElementValue(element, nullable.Target, value);
            return;
        }

        switch (resolved.Kind)
        {
            case ShapeKind.Structure:
                WriteStructure(element, (IFunctionalStructSchema)resolved, value);
                break;
            case ShapeKind.List:
            case ShapeKind.Set:
                WriteList(element, (IFunctionalListSchema)resolved, value);
                break;
            case ShapeKind.Map:
                WriteMap(element, (IFunctionalMapSchema)resolved, value);
                break;
            case ShapeKind.Union:
                WriteUnion(element, (IFunctionalUnionSchema)resolved, value);
                break;
            case ShapeKind.Document:
                throw new NotSupportedException("Smithy Document values are not supported in XML.");
            default:
                element.Value = FormatScalar(resolved, null, value);
                break;
        }
    }

    private static void WriteStructure(
        XElement element,
        IFunctionalStructSchema schema,
        object value
    )
    {
        foreach (var member in schema.Members)
        {
            var memberValue = member.GetObject(value);
            if (memberValue is null && !member.IsRequired)
            {
                continue;
            }

            if (XmlTraits.IsXmlAttribute(member))
            {
                element.SetAttributeValue(
                    ElementName(member),
                    FormatScalar(member.Target, member.Traits, memberValue)
                );
                continue;
            }

            if (XmlTraits.IsXmlFlattened(member))
            {
                WriteFlattenedMember(element, member, memberValue);
                continue;
            }

            var child = new XElement(ElementName(member));
            WriteElementValue(child, member.Target, memberValue);
            element.Add(child);
        }
    }

    private static void WriteFlattenedMember(
        XElement parent,
        IFunctionalMemberSchema member,
        object? value
    )
    {
        var target = member.Target.Resolved;
        if (value is null)
        {
            return;
        }

        if (target is IFunctionalListSchema listSchema)
        {
            foreach (var item in listSchema.GetElementsObject(value))
            {
                var child = new XElement(ElementName(member));
                WriteElementValue(child, listSchema.Element, item);
                parent.Add(child);
            }

            return;
        }

        if (target is IFunctionalMapSchema mapSchema)
        {
            foreach (var entry in mapSchema.GetEntriesObject(value))
            {
                var child = new XElement(ElementName(member));
                child.Add(new XElement("key", entry.Key));
                var valueElement = new XElement("value");
                WriteElementValue(valueElement, mapSchema.Value, entry.Value);
                child.Add(valueElement);
                parent.Add(child);
            }

            return;
        }

        var element = new XElement(ElementName(member));
        WriteElementValue(element, member.Target, value);
        parent.Add(element);
    }

    private static void WriteUnion(XElement element, IFunctionalUnionSchema schema, object value)
    {
        var @case = schema.GetCaseObject(value);
        var child = new XElement(@case.Name);
        WriteElementValue(child, @case.Target, @case.GetObject(value));
        element.Add(child);
    }

    private static void WriteList(XElement element, IFunctionalListSchema schema, object value)
    {
        var itemName = ListItemName(schema);
        foreach (var item in schema.GetElementsObject(value))
        {
            var child = new XElement(itemName);
            WriteElementValue(child, schema.Element, item);
            element.Add(child);
        }
    }

    private static void WriteMap(XElement element, IFunctionalMapSchema schema, object value)
    {
        foreach (var entry in schema.GetEntriesObject(value))
        {
            var entryElement = new XElement("entry");
            entryElement.Add(new XElement("key", entry.Key));
            var valueElement = new XElement("value");
            WriteElementValue(valueElement, schema.Value, entry.Value);
            entryElement.Add(valueElement);
            element.Add(entryElement);
        }
    }

    private static object? ReadValue(FunctionalSchema schema, XElement? element)
    {
        var resolved = schema.Resolved;
        if (element is null)
        {
            return null;
        }

        if (resolved is IFunctionalNullableSchema nullable)
        {
            return ReadValue(nullable.Target, element);
        }

        return resolved.Kind switch
        {
            ShapeKind.Boolean => string.Equals(
                element.Value,
                "true",
                StringComparison.OrdinalIgnoreCase
            ),
            ShapeKind.Byte => sbyte.Parse(element.Value, CultureInfo.InvariantCulture),
            ShapeKind.Short => short.Parse(element.Value, CultureInfo.InvariantCulture),
            ShapeKind.Integer => int.Parse(element.Value, CultureInfo.InvariantCulture),
            ShapeKind.Long => long.Parse(element.Value, CultureInfo.InvariantCulture),
            ShapeKind.Float => float.Parse(element.Value, CultureInfo.InvariantCulture),
            ShapeKind.Double => double.Parse(element.Value, CultureInfo.InvariantCulture),
            ShapeKind.BigInteger => BigInteger.Parse(element.Value, CultureInfo.InvariantCulture),
            ShapeKind.BigDecimal => decimal.Parse(element.Value, CultureInfo.InvariantCulture),
            ShapeKind.String => element.Value,
            ShapeKind.Enum => ((IFunctionalStringEnumSchema)resolved).CreateObject(element.Value),
            ShapeKind.IntEnum => ((IFunctionalIntEnumSchema)resolved).CreateObject(
                int.Parse(element.Value, CultureInfo.InvariantCulture)
            ),
            ShapeKind.Blob => Convert.FromBase64String(element.Value),
            ShapeKind.Timestamp => ParseTimestamp(resolved, null, element.Value),
            ShapeKind.Document => throw new NotSupportedException(
                "Smithy Document values are not supported in XML."
            ),
            ShapeKind.Structure => ReadStructure((IFunctionalStructSchema)resolved, element),
            ShapeKind.Union => ReadUnion((IFunctionalUnionSchema)resolved, element),
            ShapeKind.List or ShapeKind.Set => ReadList((IFunctionalListSchema)resolved, element),
            ShapeKind.Map => ReadMap((IFunctionalMapSchema)resolved, element),
            _ => throw new NotSupportedException(
                $"XML codec does not support schema kind '{resolved.Kind}'."
            ),
        };
    }

    private static object ReadStructure(IFunctionalStructSchema schema, XElement element)
    {
        var builder = schema.CreateBuilder();
        foreach (var member in schema.Members)
        {
            if (XmlTraits.IsXmlAttribute(member))
            {
                var attr = element.Attribute(ElementName(member));
                if (attr is not null)
                {
                    member.SetObject(builder, ReadScalar(member.Target, member.Traits, attr.Value));
                }
                else if (member.IsRequired)
                {
                    throw new InvalidOperationException(
                        $"Missing required member '{member.Name}'."
                    );
                }

                continue;
            }

            if (XmlTraits.IsXmlFlattened(member))
            {
                ReadFlattenedMember(builder, element, member);
                continue;
            }

            var child = element
                .Elements()
                .FirstOrDefault(e =>
                    string.Equals(e.Name.LocalName, ElementName(member), StringComparison.Ordinal)
                );
            if (child is not null)
            {
                member.SetObject(builder, ReadValue(member.Target, child));
            }
            else if (member.IsRequired)
            {
                throw new InvalidOperationException($"Missing required member '{member.Name}'.");
            }
        }

        return schema.BuildObject(builder);
    }

    private static void ReadFlattenedMember(
        object builder,
        XElement parent,
        IFunctionalMemberSchema member
    )
    {
        var target = member.Target.Resolved;
        if (target is IFunctionalListSchema listSchema)
        {
            var listBuilder = listSchema.CreateBuilder();
            foreach (var child in parent.Elements(ElementName(member)))
            {
                listSchema.AddObject(listBuilder, ReadValue(listSchema.Element, child));
            }

            member.SetObject(builder, listSchema.BuildObject(listBuilder));
            return;
        }

        if (target is IFunctionalMapSchema mapSchema)
        {
            var mapBuilder = mapSchema.CreateBuilder();
            foreach (var child in parent.Elements(ElementName(member)))
            {
                var key = child.Element("key")?.Value;
                if (key is null)
                {
                    continue;
                }

                mapSchema.AddObject(
                    mapBuilder,
                    key,
                    ReadValue(mapSchema.Value, child.Element("value"))
                );
            }

            member.SetObject(builder, mapSchema.BuildObject(mapBuilder));
        }
    }

    private static object ReadUnion(IFunctionalUnionSchema schema, XElement element)
    {
        var child =
            element.Elements().FirstOrDefault()
            ?? throw new InvalidOperationException("Union payload was empty.");
        var @case =
            schema.GetCase(child.Name.LocalName)
            ?? throw new InvalidOperationException(
                $"Unknown union member '{child.Name.LocalName}'."
            );
        return @case.CreateObject(ReadValue(@case.Target, child));
    }

    private static object ReadList(IFunctionalListSchema schema, XElement element)
    {
        var builder = schema.CreateBuilder();
        foreach (var child in element.Elements(ListItemName(schema)))
        {
            schema.AddObject(builder, ReadValue(schema.Element, child));
        }

        return schema.BuildObject(builder);
    }

    private static object ReadMap(IFunctionalMapSchema schema, XElement element)
    {
        var builder = schema.CreateBuilder();
        foreach (var entry in element.Elements())
        {
            var key = entry.Element("key")?.Value;
            if (key is null)
            {
                continue;
            }

            schema.AddObject(builder, key, ReadValue(schema.Value, entry.Element("value")));
        }

        return schema.BuildObject(builder);
    }

    private static object? ReadScalar(
        FunctionalSchema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        string value
    )
    {
        var resolved = schema.Resolved;
        if (resolved is IFunctionalNullableSchema nullable)
        {
            return ReadScalar(nullable.Target, traits, value);
        }

        return resolved.Kind switch
        {
            ShapeKind.Boolean => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase),
            ShapeKind.Byte => sbyte.Parse(value, CultureInfo.InvariantCulture),
            ShapeKind.Short => short.Parse(value, CultureInfo.InvariantCulture),
            ShapeKind.Integer => int.Parse(value, CultureInfo.InvariantCulture),
            ShapeKind.Long => long.Parse(value, CultureInfo.InvariantCulture),
            ShapeKind.Float => float.Parse(value, CultureInfo.InvariantCulture),
            ShapeKind.Double => double.Parse(value, CultureInfo.InvariantCulture),
            ShapeKind.BigInteger => BigInteger.Parse(value, CultureInfo.InvariantCulture),
            ShapeKind.BigDecimal => decimal.Parse(value, CultureInfo.InvariantCulture),
            ShapeKind.String => value,
            ShapeKind.Enum => ((IFunctionalStringEnumSchema)resolved).CreateObject(value),
            ShapeKind.IntEnum => ((IFunctionalIntEnumSchema)resolved).CreateObject(
                int.Parse(value, CultureInfo.InvariantCulture)
            ),
            ShapeKind.Blob => Convert.FromBase64String(value),
            ShapeKind.Timestamp => ParseTimestamp(resolved, traits, value),
            _ => throw new InvalidOperationException(
                $"XML attribute value cannot target schema kind '{resolved.Kind}'."
            ),
        };
    }

    private static string FormatScalar(
        FunctionalSchema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        object? value
    )
    {
        var resolved = schema.Resolved;
        if (resolved is IFunctionalNullableSchema nullable)
        {
            return FormatScalar(nullable.Target, traits, value);
        }

        return resolved.Kind switch
        {
            ShapeKind.Boolean => (bool)value! ? "true" : "false",
            ShapeKind.Byte => ((sbyte)value!).ToString(CultureInfo.InvariantCulture),
            ShapeKind.Short => ((short)value!).ToString(CultureInfo.InvariantCulture),
            ShapeKind.Integer => ((int)value!).ToString(CultureInfo.InvariantCulture),
            ShapeKind.Long => ((long)value!).ToString(CultureInfo.InvariantCulture),
            ShapeKind.Float => ((float)value!).ToString(CultureInfo.InvariantCulture),
            ShapeKind.Double => ((double)value!).ToString(CultureInfo.InvariantCulture),
            ShapeKind.BigInteger => ((BigInteger)value!).ToString(CultureInfo.InvariantCulture),
            ShapeKind.BigDecimal => ((decimal)value!).ToString(CultureInfo.InvariantCulture),
            ShapeKind.String => (string)value!,
            ShapeKind.Enum => ((IFunctionalStringEnumValue)value!).Value,
            ShapeKind.IntEnum => ((IFunctionalIntEnumSchema)resolved)
                .GetIntegerValueObject(value!)
                .ToString(CultureInfo.InvariantCulture),
            ShapeKind.Blob => Convert.ToBase64String((byte[])value!),
            ShapeKind.Timestamp => FormatTimestamp(resolved, traits, (DateTimeOffset)value!),
            _ => throw new InvalidOperationException(
                $"XML scalar value cannot target schema kind '{resolved.Kind}'."
            ),
        };
    }

    private static string FormatTimestamp(
        FunctionalSchema schema,
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

    private static DateTimeOffset ParseTimestamp(
        FunctionalSchema schema,
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

    private static string ListItemName(IFunctionalListSchema schema) =>
        XmlTraits.GetXmlName(schema.Element) ?? schema.Element.MemberName ?? "member";

    private static string ElementName(IFunctionalMemberSchema member) =>
        XmlTraits.GetXmlName(member) ?? member.Name;

    private static string RootElementName(FunctionalSchema schema) =>
        XmlTraits.GetXmlName(schema) ?? schema.Id.Name;
}
