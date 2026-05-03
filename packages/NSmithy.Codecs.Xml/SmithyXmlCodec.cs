using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using NSmithy.Core;
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

        var doc = XDocument.Load(new System.IO.MemoryStream(bytes));
        return new XmlShapeDeserializer(doc.Root);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Trait helpers
// ─────────────────────────────────────────────────────────────────────────────

internal static class XmlTraits
{
    private static readonly ShapeId XmlNameId = ShapeId.Parse("smithy.api#xmlName");
    private static readonly ShapeId XmlAttributeId = ShapeId.Parse("smithy.api#xmlAttribute");
    private static readonly ShapeId XmlFlattenedId = ShapeId.Parse("smithy.api#xmlFlattened");
    private static readonly ShapeId TimestampFormatId = ShapeId.Parse("smithy.api#timestampFormat");

    public static string? GetXmlName(Schema schema) =>
        schema.GetTrait(XmlNameId) is { HasValue: true } t ? t.Value.AsString() : null;

    public static bool IsXmlAttribute(Schema schema) => schema.HasTrait(XmlAttributeId);

    public static bool IsXmlFlattened(Schema schema) => schema.HasTrait(XmlFlattenedId);

    public static string? GetTimestampFormat(Schema schema) =>
        schema.GetTrait(TimestampFormatId) is { HasValue: true } t ? t.Value.AsString() : null;

    public static string ElementName(Schema memberSchema) =>
        GetXmlName(memberSchema) ?? memberSchema.MemberName!;

    public static string RootElementName(Schema schema) => GetXmlName(schema) ?? schema.Id.Name;
}

// ─────────────────────────────────────────────────────────────────────────────
// Serializer (top-level / value-context)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Top-level XML serializer. Writes a single root element to the sink stream.
/// </summary>
internal sealed class XmlShapeSerializer : IShapeSerializer
{
    private readonly Stream sink;
    private XElement? rootElement;

    public XmlShapeSerializer(Stream sink)
    {
        this.sink = sink;
    }

    // Value-context ctor used by nested serializers sharing a parent element
    internal XmlShapeSerializer(XElement parent)
    {
        sink = null!;
        rootElement = parent;
    }

    public void Dispose()
    {
        if (rootElement is not null && sink is not null)
        {
            Flush();
        }
    }

    public void Flush()
    {
        if (rootElement is not null && sink is not null)
        {
            var doc = new XDocument(rootElement);
            doc.Save(sink, SaveOptions.DisableFormatting);
        }
        else
        {
            sink?.Flush();
        }
    }

    public void WriteStruct(Schema schema, ISerializableStruct value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var element = new XElement(XmlTraits.RootElementName(schema));
        SetRoot(element);
        var memberSerializer = new XmlStructMemberSerializer(element);
        value.SerializeMembers(memberSerializer);
    }

    public void WriteList<TState>(
        Schema schema,
        TState state,
        int size,
        Action<TState, IShapeSerializer> consumer
    )
    {
        ArgumentNullException.ThrowIfNull(consumer);
        // Top-level list: wrap in an element named by the schema
        var element = new XElement(XmlTraits.RootElementName(schema));
        SetRoot(element);
        consumer(state, new XmlListItemSerializer(element, schema));
    }

    public void WriteMap<TState>(
        Schema schema,
        TState state,
        int size,
        Action<TState, IMapSerializer> consumer
    )
    {
        ArgumentNullException.ThrowIfNull(consumer);
        var element = new XElement(XmlTraits.RootElementName(schema));
        SetRoot(element);
        consumer(state, new XmlMapSerializer(element));
    }

    private void SetRoot(XElement element)
    {
        rootElement = element;
    }

    // Scalar write methods at top level — just serialize as root element content
    public void WriteBoolean(Schema schema, bool value) =>
        SetRootText(schema, value ? "true" : "false");

    public void WriteByte(Schema schema, sbyte value) =>
        SetRootText(schema, value.ToString(CultureInfo.InvariantCulture));

    public void WriteShort(Schema schema, short value) =>
        SetRootText(schema, value.ToString(CultureInfo.InvariantCulture));

    public void WriteInteger(Schema schema, int value) =>
        SetRootText(schema, value.ToString(CultureInfo.InvariantCulture));

    public void WriteLong(Schema schema, long value) =>
        SetRootText(schema, value.ToString(CultureInfo.InvariantCulture));

    public void WriteFloat(Schema schema, float value) =>
        SetRootText(schema, value.ToString(CultureInfo.InvariantCulture));

    public void WriteDouble(Schema schema, double value) =>
        SetRootText(schema, value.ToString(CultureInfo.InvariantCulture));

    public void WriteBigInteger(Schema schema, BigInteger value) =>
        SetRootText(schema, value.ToString(CultureInfo.InvariantCulture));

    public void WriteBigDecimal(Schema schema, decimal value) =>
        SetRootText(schema, value.ToString(CultureInfo.InvariantCulture));

    public void WriteString(Schema schema, string value) => SetRootText(schema, value);

    public void WriteBlob(Schema schema, ReadOnlySpan<byte> value) =>
        SetRootText(schema, Convert.ToBase64String(value));

    public void WriteTimestamp(Schema schema, DateTimeOffset value) =>
        SetRootText(schema, FormatTimestamp(schema, value));

    public void WriteDocument(Schema schema, Document value) =>
        throw new NotSupportedException("Smithy Document values are not supported in XML.");

    public void WriteNull(Schema schema) { }

    private void SetRootText(Schema schema, string text)
    {
        var element = new XElement(XmlTraits.RootElementName(schema), text);
        SetRoot(element);
    }

    internal static string FormatTimestamp(Schema schema, DateTimeOffset value)
    {
        var fmt = XmlTraits.GetTimestampFormat(schema?.Target ?? schema!);
        return fmt switch
        {
            "epoch-seconds" => (value.ToUnixTimeMilliseconds() / 1000.0).ToString(
                CultureInfo.InvariantCulture
            ),
            "http-date" => value.ToString("r", CultureInfo.InvariantCulture),
            _ => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), // date-time / default
        };
    }

    internal static DateTimeOffset ParseTimestamp(Schema schema, string text)
    {
        var fmt = XmlTraits.GetTimestampFormat(schema?.Target ?? schema!);
        return fmt switch
        {
            "epoch-seconds" => DateTimeOffset.FromUnixTimeMilliseconds(
                (long)(double.Parse(text, CultureInfo.InvariantCulture) * 1000)
            ),
            "http-date" => DateTimeOffset.ParseExact(
                text,
                "r",
                CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None
            ),
            _ => DateTimeOffset.Parse(
                text,
                CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind
            ),
        };
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Member-context serializer
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Member-context serializer. Each Write* call creates an XElement (or XAttribute) named by
/// the member schema and adds it to the parent element.
/// </summary>
internal sealed class XmlStructMemberSerializer(XElement parent) : IShapeSerializer
{
    public void Dispose() { }

    public void Flush() { }

    public void WriteStruct(Schema schema, ISerializableStruct value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var child = new XElement(XmlTraits.ElementName(schema));
        parent.Add(child);
        value.SerializeMembers(new XmlStructMemberSerializer(child));
    }

    public void WriteList<TState>(
        Schema schema,
        TState state,
        int size,
        Action<TState, IShapeSerializer> consumer
    )
    {
        ArgumentNullException.ThrowIfNull(consumer);
        if (XmlTraits.IsXmlFlattened(schema))
        {
            // Flattened: each item becomes a direct child of the parent named by member's xmlName
            consumer(state, new XmlListItemSerializer(parent, schema));
        }
        else
        {
            // Wrapped: items go inside a container element
            var container = new XElement(XmlTraits.ElementName(schema));
            parent.Add(container);
            consumer(state, new XmlListItemSerializer(container, schema));
        }
    }

    public void WriteMap<TState>(
        Schema schema,
        TState state,
        int size,
        Action<TState, IMapSerializer> consumer
    )
    {
        ArgumentNullException.ThrowIfNull(consumer);
        if (XmlTraits.IsXmlFlattened(schema))
        {
            consumer(state, new XmlMapSerializer(parent, XmlTraits.ElementName(schema)));
        }
        else
        {
            var container = new XElement(XmlTraits.ElementName(schema));
            parent.Add(container);
            consumer(state, new XmlMapSerializer(container));
        }
    }

    public void WriteBoolean(Schema schema, bool value) =>
        WriteScalar(schema, value ? "true" : "false");

    public void WriteByte(Schema schema, sbyte value) =>
        WriteScalar(schema, value.ToString(CultureInfo.InvariantCulture));

    public void WriteShort(Schema schema, short value) =>
        WriteScalar(schema, value.ToString(CultureInfo.InvariantCulture));

    public void WriteInteger(Schema schema, int value) =>
        WriteScalar(schema, value.ToString(CultureInfo.InvariantCulture));

    public void WriteLong(Schema schema, long value) =>
        WriteScalar(schema, value.ToString(CultureInfo.InvariantCulture));

    public void WriteFloat(Schema schema, float value) =>
        WriteScalar(schema, value.ToString(CultureInfo.InvariantCulture));

    public void WriteDouble(Schema schema, double value) =>
        WriteScalar(schema, value.ToString(CultureInfo.InvariantCulture));

    public void WriteBigInteger(Schema schema, BigInteger value) =>
        WriteScalar(schema, value.ToString(CultureInfo.InvariantCulture));

    public void WriteBigDecimal(Schema schema, decimal value) =>
        WriteScalar(schema, value.ToString(CultureInfo.InvariantCulture));

    public void WriteString(Schema schema, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WriteScalar(schema, value);
    }

    public void WriteBlob(Schema schema, ReadOnlySpan<byte> value) =>
        WriteScalar(schema, Convert.ToBase64String(value));

    public void WriteTimestamp(Schema schema, DateTimeOffset value) =>
        WriteScalar(schema, XmlShapeSerializer.FormatTimestamp(schema, value));

    public void WriteDocument(Schema schema, Document value) =>
        throw new NotSupportedException("Smithy Document values are not supported in XML.");

    public void WriteNull(Schema schema) { }

    private void WriteScalar(Schema memberSchema, string text)
    {
        if (XmlTraits.IsXmlAttribute(memberSchema))
        {
            parent.SetAttributeValue(XmlTraits.ElementName(memberSchema), text);
        }
        else
        {
            parent.Add(new XElement(XmlTraits.ElementName(memberSchema), text));
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// List item serializer
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Writes each list item as a child element of <paramref name="parent"/>.
/// The element name comes from the list member schema's xmlName or the member name "member".
/// </summary>
internal sealed class XmlListItemSerializer(XElement parent, Schema listSchema) : IShapeSerializer
{
    private string ItemElementName()
    {
        // For flattened lists the item name is the member's xmlName
        // For wrapped lists it's the list's member element name (usually "member")
        var memberSchema = listSchema.ListMember;
        if (memberSchema is not null)
        {
            return XmlTraits.GetXmlName(memberSchema) ?? memberSchema.MemberName ?? "member";
        }

        // Fallback: use the member schema's xmlName trait or "member"
        return XmlTraits.GetXmlName(listSchema) ?? "member";
    }

    public void Dispose() { }

    public void Flush() { }

    public void WriteStruct(Schema schema, ISerializableStruct value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var child = new XElement(ItemElementName());
        parent.Add(child);
        value.SerializeMembers(new XmlStructMemberSerializer(child));
    }

    public void WriteList<TState>(
        Schema schema,
        TState state,
        int size,
        Action<TState, IShapeSerializer> consumer
    )
    {
        ArgumentNullException.ThrowIfNull(consumer);
        var child = new XElement(ItemElementName());
        parent.Add(child);
        consumer(state, new XmlListItemSerializer(child, schema));
    }

    public void WriteMap<TState>(
        Schema schema,
        TState state,
        int size,
        Action<TState, IMapSerializer> consumer
    )
    {
        ArgumentNullException.ThrowIfNull(consumer);
        var child = new XElement(ItemElementName());
        parent.Add(child);
        consumer(state, new XmlMapSerializer(child));
    }

    public void WriteBoolean(Schema schema, bool value) =>
        parent.Add(new XElement(ItemElementName(), value ? "true" : "false"));

    public void WriteByte(Schema schema, sbyte value) =>
        parent.Add(new XElement(ItemElementName(), value.ToString(CultureInfo.InvariantCulture)));

    public void WriteShort(Schema schema, short value) =>
        parent.Add(new XElement(ItemElementName(), value.ToString(CultureInfo.InvariantCulture)));

    public void WriteInteger(Schema schema, int value) =>
        parent.Add(new XElement(ItemElementName(), value.ToString(CultureInfo.InvariantCulture)));

    public void WriteLong(Schema schema, long value) =>
        parent.Add(new XElement(ItemElementName(), value.ToString(CultureInfo.InvariantCulture)));

    public void WriteFloat(Schema schema, float value) =>
        parent.Add(new XElement(ItemElementName(), value.ToString(CultureInfo.InvariantCulture)));

    public void WriteDouble(Schema schema, double value) =>
        parent.Add(new XElement(ItemElementName(), value.ToString(CultureInfo.InvariantCulture)));

    public void WriteBigInteger(Schema schema, BigInteger value) =>
        parent.Add(new XElement(ItemElementName(), value.ToString(CultureInfo.InvariantCulture)));

    public void WriteBigDecimal(Schema schema, decimal value) =>
        parent.Add(new XElement(ItemElementName(), value.ToString(CultureInfo.InvariantCulture)));

    public void WriteString(Schema schema, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        parent.Add(new XElement(ItemElementName(), value));
    }

    public void WriteBlob(Schema schema, ReadOnlySpan<byte> value) =>
        parent.Add(new XElement(ItemElementName(), Convert.ToBase64String(value)));

    public void WriteTimestamp(Schema schema, DateTimeOffset value) =>
        parent.Add(
            new XElement(ItemElementName(), XmlShapeSerializer.FormatTimestamp(schema, value))
        );

    public void WriteDocument(Schema schema, Document value) =>
        throw new NotSupportedException("Smithy Document values are not supported in XML.");

    public void WriteNull(Schema schema) { }
}

// ─────────────────────────────────────────────────────────────────────────────
// Map serializer
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class XmlMapSerializer : IMapSerializer
{
    private readonly XElement parent;
    private readonly string entryElementName;

    public XmlMapSerializer(XElement parent, string entryElementName = "entry")
    {
        this.parent = parent;
        this.entryElementName = entryElementName;
    }

    public void Entry<TState>(
        string key,
        TState state,
        Action<TState, IShapeSerializer> valueWriter
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(valueWriter);
        var entry = new XElement(entryElementName);
        entry.Add(new XElement("key", key));
        var valueElement = new XElement("value");
        entry.Add(valueElement);
        parent.Add(entry);
        valueWriter(state, new XmlShapeSerializer(valueElement));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Deserializer
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class XmlShapeDeserializer : IShapeDeserializer
{
    private XElement? current;

    public XmlShapeDeserializer(XElement? root)
    {
        current = root;
    }

    public void Dispose() { }

    public bool IsNull() => current is null;

    public void ReadNull() { }

    public int ContainerSize() => current?.Elements().Count() ?? 0;

    public void ReadStruct<TState>(
        Schema schema,
        TState state,
        StructMemberConsumer<TState> consumer
    )
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(consumer.Member);

        if (current is null)
        {
            return;
        }

        var saved = current;
        try
        {
            // Read attributes first
            foreach (var attr in saved.Attributes())
            {
                var memberSchema = ResolveMember(schema, attr.Name.LocalName);
                if (memberSchema is null)
                {
                    continue;
                }

                // Create a pseudo-element for the attribute value
                current = new XElement(attr.Name.LocalName, attr.Value);
                consumer.Member(state, memberSchema, this);
            }

            // Read child elements
            foreach (var child in saved.Elements())
            {
                var memberSchema = ResolveMember(schema, child.Name.LocalName);
                current = child;
                if (memberSchema is null)
                {
                    consumer.UnknownMember?.Invoke(state, child.Name.LocalName, this);
                }
                else
                {
                    consumer.Member(state, memberSchema, this);
                }
            }
        }
        finally
        {
            current = saved;
        }
    }

    public void ReadList<TState>(Schema schema, TState state, ListMemberConsumer<TState> consumer)
    {
        ArgumentNullException.ThrowIfNull(consumer);

        if (current is null)
        {
            return;
        }

        var saved = current;
        try
        {
            // Get the item element name (e.g. "member" or from xmlName trait on list member)
            var memberSchema = schema.ListMember;
            string itemName = memberSchema is not null
                ? (XmlTraits.GetXmlName(memberSchema) ?? memberSchema.MemberName ?? "member")
                : "member";

            foreach (var child in saved.Elements(itemName))
            {
                current = child;
                consumer(state, this);
            }
        }
        finally
        {
            current = saved;
        }
    }

    public void ReadMap<TState>(Schema schema, TState state, MapMemberConsumer<TState> consumer)
    {
        ArgumentNullException.ThrowIfNull(consumer);

        if (current is null)
        {
            return;
        }

        var saved = current;
        try
        {
            // Map entries: <entry><key>k</key><value>v</value></entry>
            foreach (var entry in saved.Elements())
            {
                var keyElement = entry.Element("key");
                var valueElement = entry.Element("value");
                if (keyElement is null)
                {
                    continue;
                }

                var key = keyElement.Value;
                current = valueElement;
                consumer(state, key, this);
            }
        }
        finally
        {
            current = saved;
        }
    }

    public bool ReadBoolean(Schema schema) =>
        string.Equals(current?.Value, "true", StringComparison.OrdinalIgnoreCase);

    public sbyte ReadByte(Schema schema) =>
        sbyte.Parse(current?.Value ?? "0", CultureInfo.InvariantCulture);

    public short ReadShort(Schema schema) =>
        short.Parse(current?.Value ?? "0", CultureInfo.InvariantCulture);

    public int ReadInteger(Schema schema) =>
        int.Parse(current?.Value ?? "0", CultureInfo.InvariantCulture);

    public long ReadLong(Schema schema) =>
        long.Parse(current?.Value ?? "0", CultureInfo.InvariantCulture);

    public float ReadFloat(Schema schema) =>
        float.Parse(current?.Value ?? "0", CultureInfo.InvariantCulture);

    public double ReadDouble(Schema schema) =>
        double.Parse(current?.Value ?? "0", CultureInfo.InvariantCulture);

    public BigInteger ReadBigInteger(Schema schema) =>
        BigInteger.Parse(current?.Value ?? "0", CultureInfo.InvariantCulture);

    public decimal ReadBigDecimal(Schema schema) =>
        decimal.Parse(current?.Value ?? "0", CultureInfo.InvariantCulture);

    public string ReadString(Schema schema) =>
        current?.Value ?? throw new InvalidOperationException("Expected XML element with text.");

    public byte[] ReadBlob(Schema schema) =>
        Convert.FromBase64String(
            current?.Value ?? throw new InvalidOperationException("Expected XML element with text.")
        );

    public DateTimeOffset ReadTimestamp(Schema schema) =>
        XmlShapeSerializer.ParseTimestamp(schema, current?.Value ?? string.Empty);

    public Document ReadDocument(Schema schema) =>
        throw new NotSupportedException("Smithy Document values are not supported in XML.");

    private static Schema? ResolveMember(Schema structSchema, string xmlElementName)
    {
        foreach (var member in structSchema.Members)
        {
            var name = XmlTraits.GetXmlName(member) ?? member.MemberName!;
            if (string.Equals(name, xmlElementName, StringComparison.Ordinal))
            {
                return member;
            }
        }

        return null;
    }
}
