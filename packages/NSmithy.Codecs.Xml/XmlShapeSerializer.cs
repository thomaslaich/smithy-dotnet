using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Xml;

/// <summary>
/// Top-level XML serializer. Writes a single root element to the sink stream.
/// </summary>
internal sealed class XmlShapeSerializer : IShapeSerializer
{
    private readonly Stream sink;
    private XElement? rootElement;
    private bool flushed;

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
        sink?.Flush();
    }

    public void Flush()
    {
        if (!flushed && rootElement is not null && sink is not null)
        {
            rootElement.Save(sink, SaveOptions.DisableFormatting);
            flushed = true;
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
