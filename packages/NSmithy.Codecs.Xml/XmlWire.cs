using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Xml;

internal static class XmlWire
{
    internal sealed class XmlCodecImpl<T>(Schema<T> schema) : IXmlCodec<T>
    {
        public byte[] Serialize(T value)
        {
            var element = WriteRoot(schema, value);
            return System.Text.Encoding.UTF8.GetBytes(
                element.ToString(SaveOptions.DisableFormatting)
            );
        }

        public T Deserialize(byte[] payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            if (payload.Length == 0)
            {
                return default!;
            }

            var root = XElement.Parse(System.Text.Encoding.UTF8.GetString(payload));
            return (T)ReadValue(schema, root)!;
        }
    }

    internal static XElement WriteRoot(Schema schema, object? value)
    {
        var root = new XElement(RootElementName(schema));
        WriteElementValue(root, schema, value);
        return root;
    }

    internal static void WriteElementValue(XElement element, Schema schema, object? value)
    {
        var resolved = schema.Resolved;
        if (value is null)
        {
            return;
        }

        if (resolved is INullableSchema nullable)
        {
            WriteElementValue(element, nullable.Target, value);
            return;
        }

        switch (resolved.Kind)
        {
            case ShapeKind.Structure:
                WriteStructure(element, (IStructSchema)resolved, value);
                break;
            case ShapeKind.List:
            case ShapeKind.Set:
                WriteList(element, (IListSchema)resolved, value);
                break;
            case ShapeKind.Map:
                WriteMap(element, (IMapSchema)resolved, value);
                break;
            case ShapeKind.Union:
                WriteUnion(element, (IUnionSchema)resolved, value);
                break;
            case ShapeKind.Document:
                throw new NotSupportedException("Smithy Document values are not supported in XML.");
            default:
                element.Value = FormatScalar(resolved, null, value);
                break;
        }
    }

    internal static void WriteStructure(XElement element, IStructSchema schema, object value)
    {
        foreach (var member in schema.Members)
        {
            WriteMember(element, member, value);
        }
    }

    internal static void WriteMember(XElement element, IMemberSchema member, object value)
    {
        var memberValue = member.GetObject(value);
        if (memberValue is null && !member.IsRequired)
        {
            return;
        }

        if (XmlTraits.IsXmlAttribute(member))
        {
            element.SetAttributeValue(
                ElementName(member),
                FormatScalar(member.Target, member.MemberTraits, memberValue)
            );
            return;
        }

        if (XmlTraits.IsXmlFlattened(member))
        {
            WriteFlattenedMember(element, member, memberValue);
            return;
        }

        var child = new XElement(ElementName(member));
        WriteElementValue(child, member.Target, memberValue);
        element.Add(child);
    }

    internal static void WriteFlattenedMember(XElement parent, IMemberSchema member, object? value)
    {
        var target = member.Target.Resolved;
        if (value is null)
        {
            return;
        }

        if (target is IListSchema listSchema)
        {
            foreach (var item in listSchema.GetElementsObject(value))
            {
                var child = new XElement(ElementName(member));
                WriteElementValue(child, listSchema.Element, item);
                parent.Add(child);
            }

            return;
        }

        if (target is IMapSchema mapSchema)
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

    internal static void WriteUnion(XElement element, IUnionSchema schema, object value)
    {
        var @case = schema.GetCaseObject(value);
        var child = new XElement(@case.Name);
        WriteElementValue(child, @case.Target, @case.GetObject(value));
        element.Add(child);
    }

    internal static void WriteList(XElement element, IListSchema schema, object value)
    {
        var itemName = ListItemName(schema);
        foreach (var item in schema.GetElementsObject(value))
        {
            var child = new XElement(itemName);
            WriteElementValue(child, schema.Element, item);
            element.Add(child);
        }
    }

    internal static void WriteMap(XElement element, IMapSchema schema, object value)
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

    internal static object? ReadValue(Schema schema, XElement? element)
    {
        var resolved = schema.Resolved;
        if (element is null)
        {
            return null;
        }

        if (resolved is INullableSchema nullable)
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
            ShapeKind.Enum => ((IStringEnumSchema)resolved).CreateObject(element.Value),
            ShapeKind.IntEnum => ((IIntEnumSchema)resolved).CreateObject(
                int.Parse(element.Value, CultureInfo.InvariantCulture)
            ),
            ShapeKind.Blob => Convert.FromBase64String(element.Value),
            ShapeKind.Timestamp => ParseTimestamp(resolved, null, element.Value),
            ShapeKind.Document => throw new NotSupportedException(
                "Smithy Document values are not supported in XML."
            ),
            ShapeKind.Structure => ReadStructure((IStructSchema)resolved, element),
            ShapeKind.Union => ReadUnion((IUnionSchema)resolved, element),
            ShapeKind.List or ShapeKind.Set => ReadList((IListSchema)resolved, element),
            ShapeKind.Map => ReadMap((IMapSchema)resolved, element),
            _ => throw new NotSupportedException(
                $"XML codec does not support schema kind '{resolved.Kind}'."
            ),
        };
    }

    internal static object ReadStructure(IStructSchema schema, XElement element)
    {
        var builder = schema.CreateBuilder();
        foreach (var member in schema.Members)
        {
            ReadMember(builder, element, member);
        }

        return schema.BuildObject(builder);
    }

    internal static void ReadMember(object builder, XElement element, IMemberSchema member)
    {
        if (XmlTraits.IsXmlAttribute(member))
        {
            var attr = element.Attribute(ElementName(member));
            if (attr is not null)
            {
                member.SetObject(
                    builder,
                    ReadScalar(member.Target, member.MemberTraits, attr.Value)
                );
            }
            else if (member.IsRequired)
            {
                throw new InvalidOperationException($"Missing required member '{member.Name}'.");
            }

            return;
        }

        if (XmlTraits.IsXmlFlattened(member))
        {
            ReadFlattenedMember(builder, element, member);
            return;
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

    internal static void ReadFlattenedMember(object builder, XElement parent, IMemberSchema member)
    {
        var target = member.Target.Resolved;
        if (target is IListSchema listSchema)
        {
            var listBuilder = listSchema.CreateBuilder();
            foreach (var child in ChildElements(parent, ElementName(member)))
            {
                listSchema.AddObject(listBuilder, ReadValue(listSchema.Element, child));
            }

            member.SetObject(builder, listSchema.BuildObject(listBuilder));
            return;
        }

        if (target is IMapSchema mapSchema)
        {
            var mapBuilder = mapSchema.CreateBuilder();
            foreach (var child in ChildElements(parent, ElementName(member)))
            {
                var key = ChildElement(child, "key")?.Value;
                if (key is null)
                {
                    continue;
                }

                mapSchema.AddObject(
                    mapBuilder,
                    key,
                    ReadValue(mapSchema.Value, ChildElement(child, "value"))
                );
            }

            member.SetObject(builder, mapSchema.BuildObject(mapBuilder));
        }
    }

    internal static object ReadUnion(IUnionSchema schema, XElement element)
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

    internal static object ReadList(IListSchema schema, XElement element)
    {
        var builder = schema.CreateBuilder();
        foreach (var child in ChildElements(element, ListItemName(schema)))
        {
            schema.AddObject(builder, ReadValue(schema.Element, child));
        }

        return schema.BuildObject(builder);
    }

    internal static object ReadMap(IMapSchema schema, XElement element)
    {
        var builder = schema.CreateBuilder();
        foreach (var entry in element.Elements())
        {
            var key = ChildElement(entry, "key")?.Value;
            if (key is null)
            {
                continue;
            }

            schema.AddObject(builder, key, ReadValue(schema.Value, ChildElement(entry, "value")));
        }

        return schema.BuildObject(builder);
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

    internal static object? ReadScalar(
        Schema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        string value
    )
    {
        var resolved = schema.Resolved;
        if (resolved is INullableSchema nullable)
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
            ShapeKind.Enum => ((IStringEnumSchema)resolved).CreateObject(value),
            ShapeKind.IntEnum => ((IIntEnumSchema)resolved).CreateObject(
                int.Parse(value, CultureInfo.InvariantCulture)
            ),
            ShapeKind.Blob => Convert.FromBase64String(value),
            ShapeKind.Timestamp => ParseTimestamp(resolved, traits, value),
            _ => throw new InvalidOperationException(
                $"XML attribute value cannot target schema kind '{resolved.Kind}'."
            ),
        };
    }

    internal static string FormatScalar(
        Schema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        object? value
    )
    {
        var resolved = schema.Resolved;
        if (resolved is INullableSchema nullable)
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
            ShapeKind.Enum => ((IStringEnumValue)value!).Value,
            ShapeKind.IntEnum => ((IIntEnumSchema)resolved)
                .GetIntegerValueObject(value!)
                .ToString(CultureInfo.InvariantCulture),
            ShapeKind.Blob => Convert.ToBase64String((byte[])value!),
            ShapeKind.Timestamp => FormatTimestamp(resolved, traits, (DateTimeOffset)value!),
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
