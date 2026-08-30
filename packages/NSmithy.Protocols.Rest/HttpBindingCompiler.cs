using System.Globalization;
using System.Numerics;
using System.Text;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Protocols.Rest;

/// <summary>
/// The text forms of one HTTP-bound value type, compiled once per member from its target schema and
/// member traits. A scalar codec reads and writes one string; a list codec spreads its elements
/// over the several forms a header or query string can hold.
/// </summary>
internal interface IHttpValueCodec<T>
{
    /// <summary>The single text form: a label, a prefix-header value, one query value.</summary>
    string Format(T value);

    /// <summary>Reads the single text form; a caller's malformed text is a structured 400.</summary>
    T Parse(string value);

    /// <summary>The header form: a scalar as is, a list joined with quoting where needed.</summary>
    string FormatHeader(T value);

    T ParseHeader(string value);

    /// <summary>Appends the query pairs the value contributes: one per scalar, one per list element.</summary>
    void AppendQuery(HttpUriBuilder uri, string name, T value);

    /// <summary>Reads the values a query string carried under one name.</summary>
    T ParseMany(IReadOnlyList<string> values);
}

/// <summary>Compiles the <see cref="IHttpValueCodec{T}"/> for a bound member's target.</summary>
internal sealed class HttpBindingCompiler(IReadOnlyDictionary<ShapeId, Trait>? memberTraits)
    : PartialSchemaVisitor<object>
{
    internal static IHttpValueCodec<T> Compile<T>(
        Schema<T> target,
        IReadOnlyDictionary<ShapeId, Trait>? memberTraits
    ) => (IHttpValueCodec<T>)target.Resolved.Accept(new HttpBindingCompiler(memberTraits));

    public override object VisitBoolean(Schema<bool> schema) => new BooleanHttpCodec();

    public override object VisitByte(Schema<sbyte> schema) =>
        new NumberHttpCodec<sbyte>(ShapeKind.Byte, sbyte.Parse);

    public override object VisitShort(Schema<short> schema) =>
        new NumberHttpCodec<short>(ShapeKind.Short, short.Parse);

    public override object VisitInteger(Schema<int> schema) =>
        new NumberHttpCodec<int>(ShapeKind.Integer, int.Parse);

    public override object VisitLong(Schema<long> schema) =>
        new NumberHttpCodec<long>(ShapeKind.Long, long.Parse);

    public override object VisitFloat(Schema<float> schema) => new FloatHttpCodec();

    public override object VisitDouble(Schema<double> schema) => new DoubleHttpCodec();

    public override object VisitBigInteger(Schema<BigInteger> schema) =>
        new NumberHttpCodec<BigInteger>(ShapeKind.BigInteger, BigInteger.Parse);

    public override object VisitBigDecimal(Schema<decimal> schema) =>
        new NumberHttpCodec<decimal>(ShapeKind.BigDecimal, decimal.Parse);

    public override object VisitString(Schema<string> schema) =>
        HasTrait(schema, RestTraits.MediaType)
            ? new Base64StringHttpCodec()
            : new StringHttpCodec();

    public override object VisitBlob(Schema<byte[]> schema) => new BlobHttpCodec();

    public override object VisitTimestamp(Schema<DateTimeOffset> schema) =>
        new TimestampHttpCodec(TimestampFormat(schema));

    public override object VisitNullable<T>(NullableSchema<T> schema) =>
        new NullableHttpCodec<T>((IHttpValueCodec<T>)schema.TypedTarget.Resolved.Accept(this));

    public override object VisitList<TCollection, TElement, TBuilder>(
        IListSchema<TCollection, TElement, TBuilder> schema
    )
    {
        // The member's traits apply to each element: a header list of timestamps is a list of
        // http-dates, a @mediaType string list is a list of base64 strings.
        var element = (IHttpValueCodec<TElement>)schema.ElementSchema.Resolved.Accept(this);
        var elementKind = HttpBindingPlans.UnwrapNullable(schema.ElementSchema).Kind;
        return new ListHttpCodec<TCollection, TElement, TBuilder>(schema, element, elementKind);
    }

    public override object VisitStringEnum<T>(StringEnumSchema<T> schema) =>
        new StringEnumHttpCodec<T>(schema);

    public override object VisitIntEnum<T>(IntEnumSchema<T> schema) =>
        new IntEnumHttpCodec<T>(schema);

    protected override object VisitDefault(Schema schema) =>
        throw new NotSupportedException(
            $"HTTP bindings do not support schema kind '{schema.Kind}'."
        );

    private bool HasTrait(Schema schema, ShapeId id) =>
        memberTraits?.ContainsKey(id) == true || schema.HasTrait(id);

    private string TimestampFormat(Schema schema)
    {
        if (memberTraits?.TryGetValue(RestTraits.TimestampFormat, out var trait) == true)
        {
            return trait.Value.AsString();
        }

        if (schema.GetTrait(RestTraits.TimestampFormat) is { } shapeTrait)
        {
            return shapeTrait.Value.AsString();
        }

        return HasTrait(schema, RestTraits.HttpHeader) ? "http-date" : "date-time";
    }
}

/// <summary>
/// A codec for one text value. The list forms coincide with the single form, and a parse failure
/// is the caller's mistake: on a server a structured 400 rather than a fault.
/// </summary>
internal abstract class ScalarHttpCodec<T>(ShapeKind kind) : IHttpValueCodec<T>
{
    public abstract string Format(T value);

    protected abstract T ParseText(string value);

    public T Parse(string value)
    {
        try
        {
            return ParseText(value);
        }
        catch (Exception exception)
            when (exception is FormatException or OverflowException or ArgumentException)
        {
            throw MalformedRequestException.Serialization(
                $"Value '{value}' is not a valid {kind.ToString().ToLowerInvariant()}."
            );
        }
    }

    public string FormatHeader(T value) => Format(value);

    public T ParseHeader(string value) => Parse(value);

    public void AppendQuery(HttpUriBuilder uri, string name, T value) =>
        uri.AppendQuery(name, Format(value));

    public T ParseMany(IReadOnlyList<string> values) => Parse(values[0]);
}

internal sealed class BooleanHttpCodec() : ScalarHttpCodec<bool>(ShapeKind.Boolean)
{
    public override string Format(bool value) => value ? "true" : "false";

    // Only the two literals the model means. bool.Parse also accepts "True" and " TRUE ", which
    // would let a caller coerce a string into a boolean the model never declared.
    protected override bool ParseText(string value) =>
        value switch
        {
            "true" => true,
            "false" => false,
            _ => throw new FormatException($"'{value}' is not a boolean."),
        };
}

internal sealed class NumberHttpCodec<T>(ShapeKind kind, Func<string, IFormatProvider, T> parse)
    : ScalarHttpCodec<T>(kind)
    where T : IFormattable
{
    public override string Format(T value) => value.ToString(null, CultureInfo.InvariantCulture);

    protected override T ParseText(string value) => parse(value, CultureInfo.InvariantCulture);
}

internal sealed class FloatHttpCodec() : ScalarHttpCodec<float>(ShapeKind.Float)
{
    public override string Format(float value) => HttpValueText.FormatFloat(value);

    protected override float ParseText(string value) => HttpValueText.ParseFloat(value);
}

internal sealed class DoubleHttpCodec() : ScalarHttpCodec<double>(ShapeKind.Double)
{
    public override string Format(double value) => HttpValueText.FormatDouble(value);

    protected override double ParseText(string value) => HttpValueText.ParseDouble(value);
}

internal sealed class StringHttpCodec() : ScalarHttpCodec<string>(ShapeKind.String)
{
    public override string Format(string value) => value;

    protected override string ParseText(string value) => value;
}

/// <summary>A <c>@mediaType</c> string travels base64-encoded in headers and labels.</summary>
internal sealed class Base64StringHttpCodec() : ScalarHttpCodec<string>(ShapeKind.String)
{
    public override string Format(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    protected override string ParseText(string value) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(value));
}

internal sealed class BlobHttpCodec() : ScalarHttpCodec<byte[]>(ShapeKind.Blob)
{
    public override string Format(byte[] value) => Convert.ToBase64String(value);

    protected override byte[] ParseText(string value) => Convert.FromBase64String(value);
}

internal sealed class TimestampHttpCodec(string format)
    : ScalarHttpCodec<DateTimeOffset>(ShapeKind.Timestamp)
{
    public override string Format(DateTimeOffset value) =>
        HttpValueText.FormatTimestamp(format, value);

    protected override DateTimeOffset ParseText(string value) =>
        HttpValueText.ParseTimestamp(format, value);
}

internal sealed class StringEnumHttpCodec<T>(StringEnumSchema<T> schema)
    : ScalarHttpCodec<T>(ShapeKind.Enum)
    where T : IStringEnumValue<T>
{
    public override string Format(T value) => value.Value ?? string.Empty;

    protected override T ParseText(string value) => schema.Create(value);
}

internal sealed class IntEnumHttpCodec<T>(IntEnumSchema<T> schema)
    : ScalarHttpCodec<T>(ShapeKind.IntEnum)
    where T : struct, Enum
{
    public override string Format(T value) =>
        schema.GetIntegerValue(value).ToString(CultureInfo.InvariantCulture);

    protected override T ParseText(string value) =>
        schema.Create(int.Parse(value, CultureInfo.InvariantCulture));
}

/// <summary>An optional value type; a caller checks for null before formatting.</summary>
internal sealed class NullableHttpCodec<T>(IHttpValueCodec<T> inner) : IHttpValueCodec<T?>
    where T : struct
{
    public string Format(T? value) => inner.Format(value!.Value);

    public T? Parse(string value) => inner.Parse(value);

    public string FormatHeader(T? value) => inner.FormatHeader(value!.Value);

    public T? ParseHeader(string value) => inner.ParseHeader(value);

    public void AppendQuery(HttpUriBuilder uri, string name, T? value) =>
        inner.AppendQuery(uri, name, value!.Value);

    public T? ParseMany(IReadOnlyList<string> values) => inner.ParseMany(values);
}

/// <summary>
/// A list bound to a header or query parameter: one header joining the elements, or one query pair
/// per element. Null elements are not written.
/// </summary>
internal sealed class ListHttpCodec<TCollection, TElement, TBuilder>(
    IListSchema<TCollection, TElement, TBuilder> schema,
    IHttpValueCodec<TElement> element,
    ShapeKind elementKind
) : IHttpValueCodec<TCollection>
{
    private readonly bool quoteElements = elementKind is ShapeKind.String or ShapeKind.Enum;

    public string Format(TCollection value) =>
        throw new NotSupportedException("A list has no single HTTP text form.");

    public TCollection Parse(string value) =>
        throw new NotSupportedException("A list has no single HTTP text form.");

    public string FormatHeader(TCollection value)
    {
        var builder = new StringBuilder();
        foreach (var item in schema.GetElements(value))
        {
            if (item is null)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            var text = element.Format(item);
            builder.Append(quoteElements ? HttpValueText.QuoteHeaderListElement(text) : text);
        }

        return builder.ToString();
    }

    public TCollection ParseHeader(string value) =>
        ParseMany(HttpValueText.SplitHeaderList(value, elementKind).ToArray());

    public void AppendQuery(HttpUriBuilder uri, string name, TCollection value)
    {
        foreach (var item in schema.GetElements(value))
        {
            if (item is not null)
            {
                element.AppendQuery(uri, name, item);
            }
        }
    }

    public TCollection ParseMany(IReadOnlyList<string> values)
    {
        var builder = schema.CreateTypedBuilder();
        foreach (var value in values)
        {
            schema.Add(builder, element.Parse(value));
        }

        return schema.Build(builder);
    }
}
