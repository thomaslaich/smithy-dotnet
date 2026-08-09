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

    internal static DateTimeOffset MaterializeTimestamp(object value)
    {
        return value switch
        {
            DateTimeOffset timestamp => timestamp,
            long longValue => DateTimeOffset.FromUnixTimeSeconds(longValue),
            double doubleValue =>
            // Use ticks for sub-second precision
            new DateTimeOffset(
                DateTime.UnixEpoch.AddTicks((long)(doubleValue * TimeSpan.TicksPerSecond)),
                TimeSpan.Zero
            ),
            string text => DateTimeOffset.Parse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind
            ),
            _ => throw new InvalidOperationException(
                $"Cannot convert {value.GetType().Name} to DateTimeOffset."
            ),
        };
    }

    internal static object? ReadValue(ref CborReader reader)
    {
        var state = reader.PeekState();
        if (state == CborReaderState.Tag)
        {
            var tag = reader.ReadTag();
            var inner = ReadValue(ref reader);
            return tag switch
            {
                CborTag.UnixTimeSeconds => MaterializeTimestamp(inner!),
                CborTag.UnsignedBigNum when inner is byte[] positiveBytes => new BigInteger(
                    positiveBytes,
                    isUnsigned: true,
                    isBigEndian: true
                ),
                CborTag.NegativeBigNum when inner is byte[] negativeBytes => -1
                    - new BigInteger(negativeBytes, isUnsigned: true, isBigEndian: true),
                CborTag.DecimalFraction
                    when inner is IReadOnlyList<object?> parts && parts.Count == 2 =>
                    MaterializeDecimalFraction(parts[0], parts[1]),
                _ => inner,
            };
        }

        return state switch
        {
            CborReaderState.Null => ReadNull(ref reader),
            CborReaderState.Boolean => reader.ReadBoolean(),
            CborReaderState.UnsignedInteger => ReadUnsignedInteger(ref reader),
            CborReaderState.NegativeInteger => reader.ReadInt64(),
            CborReaderState.SinglePrecisionFloat => reader.ReadSingle(),
            CborReaderState.DoublePrecisionFloat => reader.ReadDouble(),
            CborReaderState.HalfPrecisionFloat => (double)reader.ReadHalf(),
            CborReaderState.TextString or CborReaderState.StartIndefiniteLengthTextString =>
                reader.ReadTextString(),
            CborReaderState.ByteString or CborReaderState.StartIndefiniteLengthByteString =>
                reader.ReadByteString(),
            CborReaderState.StartArray => ReadArray(ref reader),
            CborReaderState.StartMap => ReadMap(ref reader),
            _ => SkipAndReturn(ref reader),
        };
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

    internal static object? ReadNull(ref CborReader reader)
    {
        reader.ReadNull();
        return null;
    }

    internal static string SkipAndReturn(ref CborReader reader)
    {
        reader.SkipValue();
        return "<skipped>";
    }

    internal static List<object?> ReadArray(ref CborReader reader)
    {
        var count = reader.ReadStartArray();
        var list = count.HasValue ? new List<object?>(count.Value) : [];
        while (reader.PeekState() != CborReaderState.EndArray)
        {
            list.Add(ReadValue(ref reader));
        }

        reader.ReadEndArray();
        return list;
    }

    internal static Dictionary<string, object?> ReadMap(ref CborReader reader)
    {
        var count = reader.ReadStartMap();
        var dict = new Dictionary<string, object?>(
            count.HasValue ? count.Value : 8,
            StringComparer.Ordinal
        );
        while (reader.PeekState() != CborReaderState.EndMap)
        {
            dict[reader.ReadTextString()] = ReadValue(ref reader);
        }

        reader.ReadEndMap();
        return dict;
    }

    internal static decimal MaterializeDecimalFraction(object? exponentObj, object? significandObj)
    {
        var exponent = Convert.ToInt32(exponentObj, CultureInfo.InvariantCulture);
        BigInteger significand;
        if (significandObj is BigInteger bi)
            significand = bi;
        else
            significand = new BigInteger(
                Convert.ToInt64(significandObj, CultureInfo.InvariantCulture)
            );

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
