using System.Globalization;
using System.Numerics;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Xml;

/// <summary>The text form of one scalar as an XML element value or attribute.</summary>
internal interface IXmlScalar<T>
{
    string Format(T value);

    T Parse(string value);
}

/// <summary>Compiles the <see cref="IXmlScalar{T}"/> for a scalar target and its member traits.</summary>
internal static class XmlScalars
{
    internal static IXmlScalar<T> Compile<T>(
        Schema<T> schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits
    ) => (IXmlScalar<T>)schema.Resolved.Accept(new Compiler(traits));

    private sealed class Compiler(IReadOnlyDictionary<ShapeId, Trait>? traits)
        : SchemaVisitor<object>
    {
        public override object VisitBoolean(Schema<bool> schema) =>
            new Scalar<bool>(
                static value => value ? "true" : "false",
                static value => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            );

        public override object VisitByte(Schema<sbyte> schema) => Number<sbyte>(sbyte.Parse);

        public override object VisitShort(Schema<short> schema) => Number<short>(short.Parse);

        public override object VisitInteger(Schema<int> schema) => Number<int>(int.Parse);

        public override object VisitLong(Schema<long> schema) => Number<long>(long.Parse);

        public override object VisitFloat(Schema<float> schema) => Number<float>(float.Parse);

        public override object VisitDouble(Schema<double> schema) => Number<double>(double.Parse);

        public override object VisitBigInteger(Schema<BigInteger> schema) =>
            Number<BigInteger>(BigInteger.Parse);

        public override object VisitBigDecimal(Schema<decimal> schema) =>
            Number<decimal>(decimal.Parse);

        public override object VisitString(Schema<string> schema) =>
            new Scalar<string>(static value => value, static value => value);

        public override object VisitBlob(Schema<byte[]> schema) =>
            new Scalar<byte[]>(Convert.ToBase64String, Convert.FromBase64String);

        public override object VisitTimestamp(Schema<DateTimeOffset> schema) =>
            XmlTraits.GetTimestampFormat(schema, traits) switch
            {
                "epoch-seconds" => new Scalar<DateTimeOffset>(
                    static value =>
                        (value.ToUnixTimeMilliseconds() / 1000.0).ToString(
                            CultureInfo.InvariantCulture
                        ),
                    static value =>
                        DateTimeOffset.FromUnixTimeMilliseconds(
                            (long)(double.Parse(value, CultureInfo.InvariantCulture) * 1000)
                        )
                ),
                "http-date" => new Scalar<DateTimeOffset>(
                    static value => value.ToString("r", CultureInfo.InvariantCulture),
                    static value =>
                        DateTimeOffset.ParseExact(
                            value,
                            "r",
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None
                        )
                ),
                _ => new Scalar<DateTimeOffset>(
                    static value =>
                        value.UtcDateTime.ToString(
                            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
                            CultureInfo.InvariantCulture
                        ),
                    static value =>
                        DateTimeOffset.Parse(
                            value,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind
                        )
                ),
            };

        public override object VisitNullable<T>(NullableSchema<T> schema) =>
            new NullableScalar<T>((IXmlScalar<T>)schema.TargetSchema.Resolved.Accept(this));

        public override object VisitStringEnum<T>(StringEnumSchema<T> schema) =>
            new Scalar<T>(static value => value.Value, schema.Create);

        public override object VisitIntEnum<T>(IntEnumSchema<T> schema) =>
            new Scalar<T>(
                value => schema.GetIntegerValue(value).ToString(CultureInfo.InvariantCulture),
                value => schema.Create(int.Parse(value, CultureInfo.InvariantCulture))
            );

        protected override object VisitDefault(Schema schema) =>
            throw new InvalidOperationException(
                $"XML scalar value cannot target schema kind '{schema.Kind}'."
            );

        private static Scalar<T> Number<T>(Func<string, IFormatProvider, T> parse)
            where T : IFormattable =>
            new(
                static value => value.ToString(null, CultureInfo.InvariantCulture),
                value => parse(value, CultureInfo.InvariantCulture)
            );
    }

    private sealed class Scalar<T>(Func<T, string> format, Func<string, T> parse) : IXmlScalar<T>
    {
        public string Format(T value) => format(value);

        public T Parse(string value) => parse(value);
    }

    private sealed class NullableScalar<T>(IXmlScalar<T> inner) : IXmlScalar<T?>
        where T : struct
    {
        public string Format(T? value) => inner.Format(value!.Value);

        public T? Parse(string value) => inner.Parse(value);
    }
}
