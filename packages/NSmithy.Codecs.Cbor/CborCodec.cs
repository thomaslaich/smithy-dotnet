using System.Formats.Cbor;
using System.Globalization;
using System.Numerics;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Cbor;

public interface ICborCodec<T> : ICodec<T> { }

public static class CborCodec
{
    public static ICborCodec<T> FromSchema<T>(Schema<T> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new CborCodecImpl<T>(schema);
    }

    private sealed class CborCodecImpl<T>(Schema<T> schema) : ICborCodec<T>
    {
        public byte[] Serialize(T value)
        {
            var writer = new CborWriter(CborConformanceMode.Lax);
            WriteValue(writer, schema, value);
            return writer.Encode();
        }

        public T Deserialize(byte[] payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            if (payload.Length == 0)
            {
                return default!;
            }

            var reader = new CborReader(payload, CborConformanceMode.Lax);
            var value = ReadValue(ref reader);
            return (T)Materialize(schema, value)!;
        }
    }

    private static void WriteValue(CborWriter writer, Schema schema, object? value)
    {
        var resolved = schema.Resolved;
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        if (resolved is INullableSchema nullable)
        {
            WriteValue(writer, nullable.Target, value);
            return;
        }

        switch (resolved.Kind)
        {
            case ShapeKind.Boolean:
                writer.WriteBoolean((bool)value);
                break;
            case ShapeKind.Byte:
                writer.WriteInt32((sbyte)value);
                break;
            case ShapeKind.Short:
                writer.WriteInt32((short)value);
                break;
            case ShapeKind.Integer:
                writer.WriteInt32((int)value);
                break;
            case ShapeKind.Long:
                writer.WriteInt64((long)value);
                break;
            case ShapeKind.Float:
                writer.WriteSingle((float)value);
                break;
            case ShapeKind.Double:
                writer.WriteDouble((double)value);
                break;
            case ShapeKind.BigInteger:
                WriteBigInteger(writer, (BigInteger)value);
                break;
            case ShapeKind.BigDecimal:
                WriteBigDecimal(writer, (decimal)value);
                break;
            case ShapeKind.String:
                writer.WriteTextString((string)value);
                break;
            case ShapeKind.Enum:
                writer.WriteTextString(((IStringEnumValue)value).Value);
                break;
            case ShapeKind.IntEnum:
                writer.WriteInt32(((IIntEnumSchema)resolved).GetIntegerValueObject(value));
                break;
            case ShapeKind.Blob:
                writer.WriteByteString((byte[])value);
                break;
            case ShapeKind.Timestamp:
                WriteTimestamp(writer, (DateTimeOffset)value);
                break;
            case ShapeKind.Document:
                throw new NotSupportedException(
                    "Smithy Document values are not supported by rpcv2Cbor."
                );
            case ShapeKind.Structure:
                WriteStructure(writer, (IStructSchema)resolved, value);
                break;
            case ShapeKind.Union:
                WriteUnion(writer, (IUnionSchema)resolved, value);
                break;
            case ShapeKind.List:
            case ShapeKind.Set:
                WriteList(writer, (IListSchema)resolved, value);
                break;
            case ShapeKind.Map:
                WriteMap(writer, (IMapSchema)resolved, value);
                break;
            default:
                throw new NotSupportedException(
                    $"CBOR codec does not support schema kind '{resolved.Kind}'."
                );
        }
    }

    private static void WriteStructure(CborWriter writer, IStructSchema schema, object value)
    {
        writer.WriteStartMap(null);
        foreach (var member in schema.Members)
        {
            var memberValue = member.GetObject(value);
            if (memberValue is null && !member.IsRequired)
            {
                continue;
            }

            writer.WriteTextString(member.Name);
            WriteValue(writer, member.Target, memberValue);
        }

        writer.WriteEndMap();
    }

    private static void WriteUnion(CborWriter writer, IUnionSchema schema, object value)
    {
        var @case = schema.GetCaseObject(value);
        writer.WriteStartMap(1);
        writer.WriteTextString(@case.Name);
        WriteValue(writer, @case.Target, @case.GetObject(value));
        writer.WriteEndMap();
    }

    private static void WriteList(CborWriter writer, IListSchema schema, object value)
    {
        var elements = schema.GetElementsObject(value).ToArray();
        writer.WriteStartArray(elements.Length);
        foreach (var element in elements)
        {
            WriteValue(writer, schema.Element, element);
        }

        writer.WriteEndArray();
    }

    private static void WriteMap(CborWriter writer, IMapSchema schema, object value)
    {
        var entries = schema.GetEntriesObject(value).ToArray();
        writer.WriteStartMap(entries.Length);
        foreach (var entry in entries)
        {
            writer.WriteTextString(entry.Key);
            WriteValue(writer, schema.Value, entry.Value);
        }

        writer.WriteEndMap();
    }

    private static void WriteTimestamp(CborWriter writer, DateTimeOffset value)
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

    private static void WriteBigInteger(CborWriter writer, BigInteger value)
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
                writer.WriteByteString((-1 - value).ToByteArray(isUnsigned: true, isBigEndian: true));
            }
        }
    }

    private static void WriteBigDecimal(CborWriter writer, decimal value)
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

        var significand =
            (BigInteger)lo | ((BigInteger)mid << 32) | ((BigInteger)hi << 64);
        if (isNegative)
            significand = -significand;

        writer.WriteTag(CborTag.DecimalFraction);
        writer.WriteStartArray(2);
        writer.WriteInt32(-scale);
        WriteBigInteger(writer, significand);
        writer.WriteEndArray();
    }

    private static object? Materialize(Schema schema, object? value)
    {
        var resolved = schema.Resolved;
        if (value is null)
        {
            return null;
        }

        if (resolved is INullableSchema nullable)
        {
            return Materialize(nullable.Target, value);
        }

        return resolved.Kind switch
        {
            ShapeKind.Boolean => Convert.ToBoolean(value, CultureInfo.InvariantCulture),
            ShapeKind.Byte => Convert.ToSByte(value, CultureInfo.InvariantCulture),
            ShapeKind.Short => Convert.ToInt16(value, CultureInfo.InvariantCulture),
            ShapeKind.Integer => Convert.ToInt32(value, CultureInfo.InvariantCulture),
            ShapeKind.Long => Convert.ToInt64(value, CultureInfo.InvariantCulture),
            ShapeKind.Float => Convert.ToSingle(value, CultureInfo.InvariantCulture),
            ShapeKind.Double => Convert.ToDouble(value, CultureInfo.InvariantCulture),
            ShapeKind.BigInteger => value is BigInteger bi
                ? bi
                : new BigInteger(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
            ShapeKind.BigDecimal => value is decimal d
                ? d
                : Convert.ToDecimal(value, CultureInfo.InvariantCulture),
            ShapeKind.String => (string)value,
            ShapeKind.Enum => ((IStringEnumSchema)resolved).CreateObject((string)value),
            ShapeKind.IntEnum => ((IIntEnumSchema)resolved).CreateObject(
                Convert.ToInt32(value, CultureInfo.InvariantCulture)
            ),
            ShapeKind.Blob => (byte[])value,
            ShapeKind.Timestamp => MaterializeTimestamp(value),
            ShapeKind.Document => throw new NotSupportedException(
                "Smithy Document values are not supported by rpcv2Cbor."
            ),
            ShapeKind.Structure => MaterializeStructure((IStructSchema)resolved, value),
            ShapeKind.Union => MaterializeUnion((IUnionSchema)resolved, value),
            ShapeKind.List or ShapeKind.Set => MaterializeList((IListSchema)resolved, value),
            ShapeKind.Map => MaterializeMap((IMapSchema)resolved, value),
            _ => throw new NotSupportedException(
                $"CBOR codec does not support schema kind '{resolved.Kind}'."
            ),
        };
    }

    private static object MaterializeStructure(IStructSchema schema, object value)
    {
        if (value is not IReadOnlyDictionary<string, object?> map)
        {
            throw new InvalidOperationException("Expected CBOR map for structure.");
        }

        var builder = schema.CreateBuilder();
        foreach (var member in schema.Members)
        {
            if (map.TryGetValue(member.Name, out var memberValue))
            {
                member.SetObject(builder, Materialize(member.Target, memberValue));
            }
            else if (member.IsRequired)
            {
                throw new InvalidOperationException($"Missing required member '{member.Name}'.");
            }
            else if (TryCreateDefaultValue(member.Target, member.Traits, out var defaultValue))
            {
                member.SetObject(builder, defaultValue);
            }
        }

        return schema.BuildObject(builder);
    }

    private static object MaterializeUnion(IUnionSchema schema, object value)
    {
        if (value is not IReadOnlyDictionary<string, object?> map || map.Count == 0)
        {
            throw new InvalidOperationException("Expected single-entry CBOR map for union.");
        }

        var entry = map.First();
        var @case =
            schema.GetCase(entry.Key)
            ?? throw new InvalidOperationException($"Unknown union member '{entry.Key}'.");
        return @case.CreateObject(Materialize(@case.Target, entry.Value));
    }

    private static object MaterializeList(IListSchema schema, object value)
    {
        if (value is not IReadOnlyList<object?> list)
        {
            throw new InvalidOperationException("Expected CBOR array for list.");
        }

        var builder = schema.CreateBuilder();
        foreach (var item in list)
        {
            schema.AddObject(builder, Materialize(schema.Element, item));
        }

        return schema.BuildObject(builder);
    }

    private static object MaterializeMap(IMapSchema schema, object value)
    {
        if (value is not IReadOnlyDictionary<string, object?> map)
        {
            throw new InvalidOperationException("Expected CBOR map for map shape.");
        }

        var builder = schema.CreateBuilder();
        foreach (var entry in map)
        {
            schema.AddObject(builder, entry.Key, Materialize(schema.Value, entry.Value));
        }

        return schema.BuildObject(builder);
    }

    private static DateTimeOffset MaterializeTimestamp(object value)
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

    private static object? ReadValue(ref CborReader reader)
    {
        var state = reader.PeekState();
        if (state == CborReaderState.Tag)
        {
            var tag = reader.ReadTag();
            var inner = ReadValue(ref reader);
            return tag switch
            {
                CborTag.UnixTimeSeconds => MaterializeTimestamp(inner!),
                CborTag.UnsignedBigNum when inner is byte[] positiveBytes =>
                    new BigInteger(positiveBytes, isUnsigned: true, isBigEndian: true),
                CborTag.NegativeBigNum when inner is byte[] negativeBytes =>
                    -1 - new BigInteger(negativeBytes, isUnsigned: true, isBigEndian: true),
                CborTag.DecimalFraction when inner is IReadOnlyList<object?> parts
                    && parts.Count == 2 => MaterializeDecimalFraction(parts[0], parts[1]),
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
            CborReaderState.TextString => reader.ReadTextString(),
            CborReaderState.ByteString => reader.ReadByteString(),
            CborReaderState.StartArray => ReadArray(ref reader),
            CborReaderState.StartMap => ReadMap(ref reader),
            _ => SkipAndReturn(ref reader),
        };
    }

    private static object ReadUnsignedInteger(ref CborReader reader)
    {
        var value = reader.ReadUInt64();
        if (value <= (ulong)long.MaxValue)
            return (long)value;
        // Value > long.MaxValue: return as BigInteger so Materialize can handle it
        return new BigInteger(value);
    }

    private static object? ReadNull(ref CborReader reader)
    {
        reader.ReadNull();
        return null;
    }

    private static string SkipAndReturn(ref CborReader reader)
    {
        reader.SkipValue();
        return "<skipped>";
    }

    private static List<object?> ReadArray(ref CborReader reader)
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

    private static Dictionary<string, object?> ReadMap(ref CborReader reader)
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

    private static decimal MaterializeDecimalFraction(object? exponentObj, object? significandObj)
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

    private static readonly ShapeId ClientOptionalTrait = new("smithy.api", "clientOptional");
    private static readonly ShapeId DefaultTrait = new("smithy.api", "default");

    private static bool TryCreateDefaultValue(
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

    private static object? CreateDefaultValue(Schema schema, Document value)
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

    private static object CreateDefaultList(IListSchema schema, Document value)
    {
        var builder = schema.CreateBuilder();
        foreach (var item in value.AsArray())
            schema.AddObject(builder, CreateDefaultValue(UnwrapNullable(schema.Element), item));
        return schema.BuildObject(builder);
    }

    private static object CreateDefaultMap(IMapSchema schema, Document value)
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

    private static Schema UnwrapNullable(Schema schema)
    {
        return schema.Resolved is INullableSchema nullable ? nullable.Target : schema;
    }
}
