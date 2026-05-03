using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Xml;

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
