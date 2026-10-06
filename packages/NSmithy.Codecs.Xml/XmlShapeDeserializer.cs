using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using NSmithy.Core;
using NSmithy.Core.Serde;
using static NSmithy.Codecs.Xml.XmlWire;

namespace NSmithy.Codecs.Xml;

/// <summary>
/// Reads one XML value: the element (or attribute text) that holds it, described by the plan of
/// the member it belongs to. A flattened list or map instead reads the repeated items of the
/// enclosing element named <c>itemName</c>.
/// </summary>
internal readonly struct XmlShapeDeserializer(
    XElement? element,
    string? text,
    XmlMemberPlan entry,
    XElement? container = null,
    string? itemName = null
) : IShapeDeserializer
{
    private string Text => text ?? element?.Value ?? string.Empty;

    public bool TryReadNull() => element is null && text is null;

    public bool ReadBoolean() => string.Equals(Text, "true", StringComparison.OrdinalIgnoreCase);

    public sbyte ReadByte() => sbyte.Parse(Text, CultureInfo.InvariantCulture);

    public short ReadShort() => short.Parse(Text, CultureInfo.InvariantCulture);

    public int ReadInteger() => int.Parse(Text, CultureInfo.InvariantCulture);

    public long ReadLong() => long.Parse(Text, CultureInfo.InvariantCulture);

    public float ReadFloat() => float.Parse(Text, CultureInfo.InvariantCulture);

    public double ReadDouble() => double.Parse(Text, CultureInfo.InvariantCulture);

    public BigInteger ReadBigInteger() => BigInteger.Parse(Text, CultureInfo.InvariantCulture);

    public decimal ReadBigDecimal() => decimal.Parse(Text, CultureInfo.InvariantCulture);

    public string ReadString() => Text;

    public byte[] ReadBlob() => Convert.FromBase64String(Text);

    public DateTimeOffset ReadTimestamp() => XmlTimestamps.Parse(Text, entry.TimestampFormat);

    public Document ReadDocument() =>
        throw new NotSupportedException("Smithy Document values are not supported in XML.");

    public string ReadStringEnum() => Text;

    public int ReadIntEnum() => int.Parse(Text, CultureInfo.InvariantCulture);

    public Stream ReadStream() =>
        throw new NotSupportedException("XML codec does not support streaming blob schemas.");

    public IAsyncEnumerable<TEvent> ReadEventStream<TEvent>(Schema<TEvent> eventSchema) =>
        throw new NotSupportedException("XML codec does not support event stream schemas.");

    public T ReadStruct<T, TBuilder>(IStructSchema<T, TBuilder> schema)
    {
        if (element is null)
        {
            return default!;
        }

        var builder = schema.CreateTypedBuilder();
        ReadMembers(element, entry.Shape!, schema, builder);
        return schema.Build(builder);
    }

    /// <summary>
    /// Reads each member the plan includes from its attribute or child element. An absent member
    /// is left unset, or rejected when it is required.
    /// </summary>
    internal static void ReadMembers<T, TBuilder>(
        XElement element,
        XmlShapePlan plan,
        IStructSchema<T, TBuilder> schema,
        TBuilder builder
    )
    {
        for (var index = 0; index < plan.Members.Length; index++)
        {
            if (!plan.IsIncluded(index))
            {
                continue;
            }

            var member = plan.Members[index];
            if (member.IsAttribute)
            {
                var attribute = element.Attribute(AttributeName(element, member.ElementName));
                if (attribute is not null)
                {
                    var nested = new XmlShapeDeserializer(null, attribute.Value, member);
                    schema.DeserializeMember(builder, index, ref nested);
                }
                else if (member.IsRequired)
                {
                    throw new MissingRequiredMemberException(member.Name);
                }

                continue;
            }

            if (
                member.IsFlattened
                && member.Shape?.Kind is ShapeKind.List or ShapeKind.Set or ShapeKind.Map
            )
            {
                // A flattened collection is its repeated items; none is an empty collection.
                var items = new XmlShapeDeserializer(
                    null,
                    null,
                    member,
                    element,
                    member.ElementName
                );
                schema.DeserializeMember(builder, index, ref items);
                continue;
            }

            var child = ChildElement(element, member.ElementName);
            if (
                child is null
                && !member.IsFlattened
                && string.Equals(
                    element.Name.LocalName,
                    member.ElementName,
                    StringComparison.Ordinal
                )
            )
            {
                child = element;
            }

            if (child is not null)
            {
                var nested = new XmlShapeDeserializer(child, null, member);
                schema.DeserializeMember(builder, index, ref nested);
            }
            else if (member.IsRequired)
            {
                throw new MissingRequiredMemberException(member.Name);
            }
        }
    }

    public TCollection ReadList<TCollection, TElement, TBuilder>(
        IListSchema<TCollection, TElement, TBuilder> schema
    )
    {
        var plan = entry.Shape!;
        var item = plan.Members[0];
        var builder = schema.CreateTypedBuilder();
        var items =
            container is not null ? ChildElements(container, itemName!)
            : element is not null ? ChildElements(element, item.ElementName)
            : [];
        foreach (var child in items)
        {
            var nested = new XmlShapeDeserializer(child, null, item);
            schema.DeserializeElement(builder, ref nested);
        }

        return schema.Build(builder);
    }

    public TDictionary ReadMap<TDictionary, TValue, TBuilder>(
        IMapSchema<TDictionary, TValue, TBuilder> schema
    )
    {
        var plan = entry.Shape!;
        var keyName = plan.Members[0].ElementName;
        var value = plan.Members[1];
        var builder = schema.CreateTypedBuilder();
        var entries =
            container is not null ? ChildElements(container, itemName!)
            : element is not null ? element.Elements()
            : [];
        foreach (var mapEntry in entries)
        {
            var key = ChildElement(mapEntry, keyName)?.Value;
            if (key is null)
            {
                continue;
            }

            var nested = new XmlShapeDeserializer(
                ChildElement(mapEntry, value.ElementName),
                null,
                value
            );
            schema.DeserializeEntry(builder, key, ref nested);
        }

        return schema.Build(builder);
    }

    public T ReadUnion<T>(IUnionSchema<T> schema)
    {
        var child =
            element?.Elements().FirstOrDefault()
            ?? throw new InvalidOperationException("Union payload was empty.");
        var plan = entry.Shape!;
        var index = plan.IndexOfElement(child.Name.LocalName);
        if (index < 0)
        {
            throw new InvalidOperationException($"Unknown union member '{child.Name.LocalName}'.");
        }

        var nested = new XmlShapeDeserializer(child, null, plan.Members[index]);
        return schema.DeserializeCase(index, ref nested);
    }
}
