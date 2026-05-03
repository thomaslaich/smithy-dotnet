using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Xml;

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
