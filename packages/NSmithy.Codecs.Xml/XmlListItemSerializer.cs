using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Xml;

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
