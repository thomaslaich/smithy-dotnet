using System.Formats.Cbor;
using System.Globalization;
using System.Numerics;
using NSmithy.Core;
using NSmithy.Core.Serde;
using static NSmithy.Codecs.Cbor.CborWire;

namespace NSmithy.Codecs.Cbor;

internal static class CborWire
{
    internal static void WriteTimestamp(CborWriter writer, DateTimeOffset value)
    {
        writer.WriteTag(CborTag.UnixTimeSeconds);
        var ticks = value.ToUniversalTime().Ticks - DateTimeOffset.UnixEpoch.Ticks;
        var wholeSec = ticks / TimeSpan.TicksPerSecond;
        var subTicks = ticks % TimeSpan.TicksPerSecond;
        if (subTicks == 0)
        {
            writer.WriteInt64(wholeSec);
        }
        else
        {
            writer.WriteDouble((double)ticks / TimeSpan.TicksPerSecond);
        }
    }

    internal static void WriteBigInteger(CborWriter writer, BigInteger value)
    {
        if (value >= 0)
        {
            if (value <= long.MaxValue)
            {
                writer.WriteInt64((long)value);
            }
            else if (value <= ulong.MaxValue)
            {
                writer.WriteUInt64((ulong)value);
            }
            else
            {
                writer.WriteTag(CborTag.UnsignedBigNum);
                writer.WriteByteString(value.ToByteArray(isUnsigned: true, isBigEndian: true));
            }
        }
        else
        {
            if (value >= long.MinValue)
            {
                writer.WriteInt64((long)value);
            }
            else
            {
                // CBOR negative bignum stores (-1 - N)
                writer.WriteTag(CborTag.NegativeBigNum);
                writer.WriteByteString(
                    (-1 - value).ToByteArray(isUnsigned: true, isBigEndian: true)
                );
            }
        }
    }

    internal static void WriteBigDecimal(CborWriter writer, decimal value)
    {
        // Represent as CBOR decimal fraction: [exponent, significand] = significand * 10^exponent
        // C# decimal = significand * 10^(-scale), so exponent = -scale.
        var bits = decimal.GetBits(value);
        var lo = (uint)bits[0];
        var mid = (uint)bits[1];
        var hi = (uint)bits[2];
        var flags = bits[3];
        var isNegative = (flags & unchecked((int)0x80000000)) != 0;
        var scale = (byte)((flags >> 16) & 0xFF);

        var significand = (BigInteger)lo | ((BigInteger)mid << 32) | ((BigInteger)hi << 64);
        if (isNegative)
            significand = -significand;

        writer.WriteTag(CborTag.DecimalFraction);
        writer.WriteStartArray(2);
        writer.WriteInt32(-scale);
        WriteBigInteger(writer, significand);
        writer.WriteEndArray();
    }

    internal static DateTimeOffset ReadTimestamp(CborReader reader)
    {
        if (reader.PeekState() == CborReaderState.Tag)
        {
            var tag = reader.ReadTag();
            if (tag != CborTag.UnixTimeSeconds)
            {
                throw new InvalidOperationException(
                    $"Expected unix timestamp tag but found {tag}."
                );
            }
        }

        return reader.PeekState() switch
        {
            CborReaderState.UnsignedInteger => DateTimeOffset.FromUnixTimeSeconds(
                Convert.ToInt64(reader.ReadUInt64(), CultureInfo.InvariantCulture)
            ),
            CborReaderState.NegativeInteger => DateTimeOffset.FromUnixTimeSeconds(
                reader.ReadInt64()
            ),
            CborReaderState.SinglePrecisionFloat => MaterializeTimestamp(reader.ReadSingle()),
            CborReaderState.DoublePrecisionFloat => MaterializeTimestamp(reader.ReadDouble()),
            CborReaderState.HalfPrecisionFloat => MaterializeTimestamp((double)reader.ReadHalf()),
            CborReaderState.TextString or CborReaderState.StartIndefiniteLengthTextString =>
                DateTimeOffset.Parse(
                    reader.ReadTextString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind
                ),
            _ => throw new InvalidOperationException(
                $"Cannot convert CBOR {reader.PeekState()} to DateTimeOffset."
            ),
        };
    }

    internal static BigInteger ReadBigInteger(CborReader reader)
    {
        if (reader.PeekState() != CborReaderState.Tag)
        {
            return ReadIntegerAsBigInteger(reader);
        }

        var tag = reader.ReadTag();
        return tag switch
        {
            CborTag.UnsignedBigNum => new BigInteger(
                reader.ReadByteString(),
                isUnsigned: true,
                isBigEndian: true
            ),
            CborTag.NegativeBigNum => -1
                - new BigInteger(reader.ReadByteString(), isUnsigned: true, isBigEndian: true),
            _ => throw new InvalidOperationException($"Expected bignum tag but found {tag}."),
        };
    }

    internal static decimal ReadBigDecimal(CborReader reader)
    {
        if (reader.PeekState() != CborReaderState.Tag)
        {
            return reader.PeekState() switch
            {
                CborReaderState.UnsignedInteger => Convert.ToDecimal(
                    reader.ReadUInt64(),
                    CultureInfo.InvariantCulture
                ),
                CborReaderState.NegativeInteger => Convert.ToDecimal(
                    reader.ReadInt64(),
                    CultureInfo.InvariantCulture
                ),
                CborReaderState.SinglePrecisionFloat => Convert.ToDecimal(
                    reader.ReadSingle(),
                    CultureInfo.InvariantCulture
                ),
                CborReaderState.DoublePrecisionFloat => Convert.ToDecimal(
                    reader.ReadDouble(),
                    CultureInfo.InvariantCulture
                ),
                CborReaderState.HalfPrecisionFloat => Convert.ToDecimal(
                    reader.ReadHalf(),
                    CultureInfo.InvariantCulture
                ),
                _ => throw new InvalidOperationException(
                    $"Cannot convert CBOR {reader.PeekState()} to decimal."
                ),
            };
        }

        var tag = reader.ReadTag();
        if (tag != CborTag.DecimalFraction)
        {
            throw new InvalidOperationException($"Expected decimal fraction tag but found {tag}.");
        }

        var length = reader.ReadStartArray();
        if (length is not 2)
        {
            throw new InvalidOperationException("CBOR decimal fraction must contain two items.");
        }

        var exponent = Convert.ToInt32(ReadInteger(reader), CultureInfo.InvariantCulture);
        var significand = ReadBigInteger(reader);
        reader.ReadEndArray();
        return MaterializeDecimalFraction(exponent, significand);
    }

    internal static object ReadUnsignedInteger(ref CborReader reader)
    {
        var value = reader.ReadUInt64();
        if (value <= (ulong)long.MaxValue)
            return (long)value;
        // Value > long.MaxValue: return as BigInteger so Materialize can handle it
        return new BigInteger(value);
    }

    internal static object ReadInteger(CborReader reader) =>
        reader.PeekState() == CborReaderState.UnsignedInteger
            ? ReadUnsignedInteger(ref reader)
            : reader.ReadInt64();

    private static BigInteger ReadIntegerAsBigInteger(CborReader reader) =>
        reader.PeekState() switch
        {
            CborReaderState.UnsignedInteger => new BigInteger(reader.ReadUInt64()),
            CborReaderState.NegativeInteger => new BigInteger(reader.ReadInt64()),
            _ => throw new InvalidOperationException(
                $"Cannot convert CBOR {reader.PeekState()} to BigInteger."
            ),
        };

    internal static string ReadNullableTextString(CborReader reader)
    {
        if (reader.PeekState() != CborReaderState.Null)
        {
            return reader.ReadTextString();
        }

        reader.ReadNull();
        return null!;
    }

    internal static byte[] ReadNullableByteString(CborReader reader)
    {
        if (reader.PeekState() != CborReaderState.Null)
        {
            return reader.ReadByteString();
        }

        reader.ReadNull();
        return null!;
    }

    private static DateTimeOffset MaterializeTimestamp(double seconds) =>
        new(DateTime.UnixEpoch.AddTicks((long)(seconds * TimeSpan.TicksPerSecond)), TimeSpan.Zero);

    internal static decimal MaterializeDecimalFraction(int exponent, BigInteger significand)
    {
        // CBOR decimal fraction: significand * 10^exponent
        // C# decimal: significand * 10^(-scale) where scale is 0..28
        var scale = -exponent;
        if (scale < 0)
        {
            // Positive exponent: absorb into significand
            for (var i = 0; i < -scale; i++)
                significand *= 10;
            scale = 0;
        }

        if (scale > 28)
            throw new OverflowException(
                $"Decimal fraction exponent {exponent} exceeds System.Decimal precision."
            );

        var isNegative = significand < 0;
        if (isNegative)
            significand = -significand;

        var lo = (uint)(significand & 0xFFFFFFFF);
        var mid = (uint)((significand >> 32) & 0xFFFFFFFF);
        var hi = (uint)((significand >> 64) & 0xFFFFFFFF);

        return new decimal((int)lo, (int)mid, (int)hi, isNegative, (byte)scale);
    }

    // ---- Default value support ----

    internal static readonly ShapeId ClientOptionalTrait = new("smithy.api", "clientOptional");
    internal static readonly ShapeId DefaultTrait = new("smithy.api", "default");

    internal static bool TryCreateDefaultValue(
        Schema schema,
        IReadOnlyDictionary<ShapeId, Trait> traits,
        out object? value
    )
    {
        if (
            traits.ContainsKey(ClientOptionalTrait)
            || !traits.TryGetValue(DefaultTrait, out var trait)
            || trait.Value.Kind == DocumentKind.Null
        )
        {
            value = null;
            return false;
        }

        value = CreateDefaultValue(UnwrapNullable(schema), trait.Value);
        return value is not null;
    }

    internal static object? CreateDefaultValue(Schema schema, Document value)
    {
        return schema.Kind switch
        {
            ShapeKind.Boolean => value.AsBoolean(),
            ShapeKind.Byte => (sbyte)value.AsNumber(),
            ShapeKind.Short => (short)value.AsNumber(),
            ShapeKind.Integer => (int)value.AsNumber(),
            ShapeKind.Long => (long)value.AsNumber(),
            ShapeKind.Float => (float)value.AsNumber(),
            ShapeKind.Double => (double)value.AsNumber(),
            ShapeKind.BigInteger => new BigInteger(value.AsNumber()),
            ShapeKind.BigDecimal => value.AsNumber(),
            ShapeKind.String => value.AsString(),
            ShapeKind.Enum => ((IStringEnumSchema)schema).CreateObject(value.AsString()),
            ShapeKind.IntEnum => ((IIntEnumSchema)schema).CreateObject((int)value.AsNumber()),
            ShapeKind.Blob => Convert.FromBase64String(value.AsString()),
            ShapeKind.Timestamp => DateTimeOffset.FromUnixTimeSeconds((long)value.AsNumber()),
            ShapeKind.Document => value,
            ShapeKind.List or ShapeKind.Set when schema.Resolved is IListSchema list =>
                CreateDefaultList(list, value),
            ShapeKind.Map when schema.Resolved is IMapSchema map => CreateDefaultMap(map, value),
            _ => null,
        };
    }

    internal static object CreateDefaultList(IListSchema schema, Document value)
    {
        var builder = schema.CreateBuilder();
        foreach (var item in value.AsArray())
            schema.AddObject(builder, CreateDefaultValue(UnwrapNullable(schema.Element), item));
        return schema.BuildObject(builder);
    }

    internal static object CreateDefaultMap(IMapSchema schema, Document value)
    {
        var builder = schema.CreateBuilder();
        foreach (var entry in value.AsObject())
            schema.AddObject(
                builder,
                entry.Key,
                CreateDefaultValue(UnwrapNullable(schema.Value), entry.Value)
            );
        return schema.BuildObject(builder);
    }

    internal static Schema UnwrapNullable(Schema schema)
    {
        return schema.Resolved is INullableSchema nullable ? nullable.Target : schema;
    }
}
