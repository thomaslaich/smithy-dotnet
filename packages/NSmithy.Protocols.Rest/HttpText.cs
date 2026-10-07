using System.Globalization;
using System.Numerics;
using System.Text;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Protocols.Rest;

/// <summary>
/// Writes a value bound to a label, query string, or header as its text forms: one per scalar, one
/// per list element, and one key with its values per map entry. Null elements and values are not
/// written.
/// </summary>
internal struct HttpTextSerializer : IShapeSerializer
{
    private readonly HttpTextFormat format;
    private readonly List<string> texts;
    private readonly List<KeyValuePair<string, List<string>>>? entries;

    // Whether this serializer receives a map's entries, whose key is member 0.
    private readonly bool keys;

    public HttpTextSerializer(
        HttpTextFormat format,
        List<string> texts,
        List<KeyValuePair<string, List<string>>>? entries = null,
        bool keys = false
    )
    {
        this.format = format;
        this.texts = texts;
        this.entries = entries;
        this.keys = keys;
    }

    private readonly void Add(string text) => (keys ? entries![^1].Value : texts).Add(text);

    public readonly bool WritesDefault(int member) => false;

    public readonly void WriteNull(int member) { }

    public readonly void WriteBoolean(int member, bool value) => Add(value ? "true" : "false");

    public readonly void WriteByte(int member, sbyte value) =>
        Add(value.ToString(CultureInfo.InvariantCulture));

    public readonly void WriteShort(int member, short value) =>
        Add(value.ToString(CultureInfo.InvariantCulture));

    public readonly void WriteInteger(int member, int value) =>
        Add(value.ToString(CultureInfo.InvariantCulture));

    public readonly void WriteLong(int member, long value) =>
        Add(value.ToString(CultureInfo.InvariantCulture));

    public readonly void WriteFloat(int member, float value) =>
        Add(HttpValueText.FormatFloat(value));

    public readonly void WriteDouble(int member, double value) =>
        Add(HttpValueText.FormatDouble(value));

    public readonly void WriteBigInteger(int member, BigInteger value) =>
        Add(value.ToString(CultureInfo.InvariantCulture));

    public readonly void WriteBigDecimal(int member, decimal value) =>
        Add(value.ToString(CultureInfo.InvariantCulture));

    public readonly void WriteString(int member, string value)
    {
        if (keys && member == 0)
        {
            entries!.Add(new(value, []));
            return;
        }

        // A @mediaType string travels base64-encoded in headers and labels.
        Add(format.Base64Strings ? Convert.ToBase64String(Encoding.UTF8.GetBytes(value)) : value);
    }

    public readonly void WriteBlob(int member, byte[] value) => Add(Convert.ToBase64String(value));

    public readonly void WriteTimestamp(int member, DateTimeOffset value) =>
        Add(HttpValueText.FormatTimestamp(format.TimestampFormat, value));

    public readonly void WriteDocument(int member, Document value) => throw Unsupported("document");

    public readonly void WriteStringEnum(int member, string value) => Add(value);

    public readonly void WriteIntEnum(int member, int value) =>
        Add(value.ToString(CultureInfo.InvariantCulture));

    public readonly void WriteStream(int member, Stream value) => throw Unsupported("blob");

    public readonly void WriteEventStream<TEvent>(
        int member,
        IAsyncEnumerable<TEvent> events,
        Schema<TEvent> eventSchema
    ) => throw Unsupported("union");

    public readonly void WriteStruct<T>(int member, T value, IStructSchema<T> schema) =>
        throw Unsupported("structure");

    public readonly void WriteList<TCollection, TElement>(
        int member,
        TCollection value,
        IListSchema<TCollection, TElement> schema
    )
    {
        var elements = new HttpTextSerializer(format, keys ? entries![^1].Value : texts);
        schema.SerializeElements(value, ref elements);
    }

    public readonly void WriteMap<TDictionary, TValue>(
        int member,
        TDictionary value,
        IMapSchema<TDictionary, TValue> schema
    )
    {
        var mapEntries = new HttpTextSerializer(
            format,
            texts,
            entries ?? throw Unsupported("map"),
            keys: true
        );
        schema.SerializeEntries(value, ref mapEntries);
    }

    public readonly void WriteUnion<T>(int member, T value, UnionSchema<T> schema) =>
        throw Unsupported("union");

    private static NotSupportedException Unsupported(string kind) =>
        new($"HTTP bindings do not support schema kind '{kind}'.");
}

/// <summary>
/// Reads a value bound to a label, query string, or header from its text forms. A caller's
/// malformed text is the caller's mistake: on a server a structured 400 rather than a fault.
/// </summary>
internal readonly struct HttpTextDeserializer : IShapeDeserializer
{
    private readonly HttpTextFormat format;
    private readonly string text;

    // The values a query string carried under one name, each a list element.
    private readonly IReadOnlyList<string>? values;

    // Whether a list's elements are joined in the one text, as a header joins them.
    private readonly bool header;

    private readonly IEnumerable<KeyValuePair<string, IReadOnlyList<string>>>? entries;

    private HttpTextDeserializer(
        HttpTextFormat format,
        string text,
        IReadOnlyList<string>? values = null,
        bool header = false,
        IEnumerable<KeyValuePair<string, IReadOnlyList<string>>>? entries = null
    )
    {
        this.format = format;
        this.text = text;
        this.values = values;
        this.header = header;
        this.entries = entries;
    }

    /// <summary>A label's or status code's single text form.</summary>
    public static HttpTextDeserializer Single(HttpTextFormat format, string text) =>
        new(format, text);

    public static HttpTextDeserializer Header(HttpTextFormat format, string text) =>
        new(format, text, header: true);

    /// <summary>The values a query string carried under one name; a scalar reads the first.</summary>
    public static HttpTextDeserializer Many(HttpTextFormat format, IReadOnlyList<string> values) =>
        new(format, values[0], values);

    /// <summary>The entries of a map bound to header prefixes or query parameters.</summary>
    public static HttpTextDeserializer Map(
        HttpTextFormat format,
        IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> entries
    ) => new(format, "", entries: entries);

    public bool TryReadNull() => false;

    // Only the two literals the model means. bool.Parse also accepts "True" and " TRUE ", which
    // would let a caller coerce a string into a boolean the model never declared.
    public bool ReadBoolean() =>
        Parse(
            ShapeKind.Boolean,
            static value =>
                value switch
                {
                    "true" => true,
                    "false" => false,
                    _ => throw new FormatException($"'{value}' is not a boolean."),
                }
        );

    public sbyte ReadByte() =>
        Parse(ShapeKind.Byte, static value => sbyte.Parse(value, CultureInfo.InvariantCulture));

    public short ReadShort() =>
        Parse(ShapeKind.Short, static value => short.Parse(value, CultureInfo.InvariantCulture));

    public int ReadInteger() =>
        Parse(ShapeKind.Integer, static value => int.Parse(value, CultureInfo.InvariantCulture));

    public long ReadLong() =>
        Parse(ShapeKind.Long, static value => long.Parse(value, CultureInfo.InvariantCulture));

    public float ReadFloat() => Parse(ShapeKind.Float, HttpValueText.ParseFloat);

    public double ReadDouble() => Parse(ShapeKind.Double, HttpValueText.ParseDouble);

    public BigInteger ReadBigInteger() =>
        Parse(
            ShapeKind.BigInteger,
            static value => BigInteger.Parse(value, CultureInfo.InvariantCulture)
        );

    public decimal ReadBigDecimal() =>
        Parse(
            ShapeKind.BigDecimal,
            static value => decimal.Parse(value, CultureInfo.InvariantCulture)
        );

    public string ReadString() =>
        format.Base64Strings
            ? Parse(
                ShapeKind.String,
                static value => Encoding.UTF8.GetString(Convert.FromBase64String(value))
            )
            : text;

    public byte[] ReadBlob() => Parse(ShapeKind.Blob, Convert.FromBase64String);

    public DateTimeOffset ReadTimestamp()
    {
        var timestampFormat = format.TimestampFormat;
        return Parse(
            ShapeKind.Timestamp,
            value => HttpValueText.ParseTimestamp(timestampFormat, value)
        );
    }

    public Document ReadDocument() => throw Unsupported("document");

    public string ReadStringEnum() => text;

    public int ReadIntEnum() =>
        Parse(ShapeKind.IntEnum, static value => int.Parse(value, CultureInfo.InvariantCulture));

    public Stream ReadStream() => throw Unsupported("blob");

    public IAsyncEnumerable<TEvent> ReadEventStream<TEvent>(Schema<TEvent> eventSchema) =>
        throw Unsupported("union");

    public T ReadStruct<T, TBuilder>(StructSchema<T, TBuilder> schema) =>
        throw Unsupported("structure");

    public TCollection ReadList<TCollection, TElement, TBuilder>(
        ListSchema<TCollection, TElement, TBuilder> schema
    )
    {
        var builder = schema.CreateTypedBuilder();
        var elements = header ? HttpValueText.SplitHeaderList(text, format.Kind) : values ?? [text];
        foreach (var element in elements)
        {
            var deserializer = Single(format, element);
            schema.DeserializeElement(builder, ref deserializer);
        }

        return schema.Build(builder);
    }

    public TDictionary ReadMap<TDictionary, TValue, TBuilder>(
        MapSchema<TDictionary, TValue, TBuilder> schema
    )
    {
        var builder = schema.CreateTypedBuilder();
        foreach (var entry in entries ?? throw Unsupported("map"))
        {
            var deserializer = Many(format, entry.Value);
            schema.DeserializeEntry(builder, entry.Key, ref deserializer);
        }

        return schema.Build(builder);
    }

    public T ReadUnion<T>(UnionSchema<T> schema) => throw Unsupported("union");

    private T Parse<T>(ShapeKind kind, Func<string, T> parse)
    {
        try
        {
            return parse(text);
        }
        catch (Exception exception)
            when (exception is FormatException or OverflowException or ArgumentException)
        {
            throw MalformedRequestException.Serialization(
                $"Value '{text}' is not a valid {kind.ToString().ToLowerInvariant()}."
            );
        }
    }

    private static NotSupportedException Unsupported(string kind) =>
        new($"HTTP bindings do not support schema kind '{kind}'.");
}
