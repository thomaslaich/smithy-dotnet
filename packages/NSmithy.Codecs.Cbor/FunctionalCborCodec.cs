using System.Formats.Cbor;
using System.Globalization;
using System.Numerics;
using NSmithy.Core;
using NSmithy.Core.Functional;

namespace NSmithy.Codecs.Cbor;

public interface IFunctionalCborCodec<T> : IFunctionalCodec<T, byte[]> { }

public static class FunctionalCborCodec
{
    public static IFunctionalCborCodec<T> FromSchema<T>(FunctionalSchema<T> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new FunctionalCborCodecImpl<T>(schema);
    }

    private sealed class FunctionalCborCodecImpl<T>(FunctionalSchema<T> schema)
        : IFunctionalCborCodec<T>
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

    private static void WriteValue(CborWriter writer, FunctionalSchema schema, object? value)
    {
        var resolved = schema.Resolved;
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        if (resolved is IFunctionalNullableSchema nullable)
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
                writer.WriteInt64((long)(BigInteger)value);
                break;
            case ShapeKind.BigDecimal:
                writer.WriteDouble((double)(decimal)value);
                break;
            case ShapeKind.String:
                writer.WriteTextString((string)value);
                break;
            case ShapeKind.Enum:
                writer.WriteTextString(((IFunctionalStringEnumValue)value).Value);
                break;
            case ShapeKind.IntEnum:
                writer.WriteInt32(
                    ((IFunctionalIntEnumSchema)resolved).GetIntegerValueObject(value)
                );
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
                WriteStructure(writer, (IFunctionalStructSchema)resolved, value);
                break;
            case ShapeKind.Union:
                WriteUnion(writer, (IFunctionalUnionSchema)resolved, value);
                break;
            case ShapeKind.List:
            case ShapeKind.Set:
                WriteList(writer, (IFunctionalListSchema)resolved, value);
                break;
            case ShapeKind.Map:
                WriteMap(writer, (IFunctionalMapSchema)resolved, value);
                break;
            default:
                throw new NotSupportedException(
                    $"CBOR codec does not support schema kind '{resolved.Kind}'."
                );
        }
    }

    private static void WriteStructure(
        CborWriter writer,
        IFunctionalStructSchema schema,
        object value
    )
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

    private static void WriteUnion(CborWriter writer, IFunctionalUnionSchema schema, object value)
    {
        var @case = schema.GetCaseObject(value);
        writer.WriteStartMap(1);
        writer.WriteTextString(@case.Name);
        WriteValue(writer, @case.Target, @case.GetObject(value));
        writer.WriteEndMap();
    }

    private static void WriteList(CborWriter writer, IFunctionalListSchema schema, object value)
    {
        var elements = schema.GetElementsObject(value).ToArray();
        writer.WriteStartArray(elements.Length);
        foreach (var element in elements)
        {
            WriteValue(writer, schema.Element, element);
        }

        writer.WriteEndArray();
    }

    private static void WriteMap(CborWriter writer, IFunctionalMapSchema schema, object value)
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
        var epochSeconds = value.ToUnixTimeMilliseconds() / 1000.0;
        if (epochSeconds == Math.Floor(epochSeconds))
        {
            writer.WriteInt64((long)epochSeconds);
        }
        else
        {
            writer.WriteDouble(epochSeconds);
        }
    }

    private static object? Materialize(FunctionalSchema schema, object? value)
    {
        var resolved = schema.Resolved;
        if (value is null)
        {
            return null;
        }

        if (resolved is IFunctionalNullableSchema nullable)
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
            ShapeKind.BigDecimal => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
            ShapeKind.String => (string)value,
            ShapeKind.Enum => ((IFunctionalStringEnumSchema)resolved).CreateObject((string)value),
            ShapeKind.IntEnum => ((IFunctionalIntEnumSchema)resolved).CreateObject(
                Convert.ToInt32(value, CultureInfo.InvariantCulture)
            ),
            ShapeKind.Blob => (byte[])value,
            ShapeKind.Timestamp => MaterializeTimestamp(value),
            ShapeKind.Document => throw new NotSupportedException(
                "Smithy Document values are not supported by rpcv2Cbor."
            ),
            ShapeKind.Structure => MaterializeStructure((IFunctionalStructSchema)resolved, value),
            ShapeKind.Union => MaterializeUnion((IFunctionalUnionSchema)resolved, value),
            ShapeKind.List or ShapeKind.Set => MaterializeList(
                (IFunctionalListSchema)resolved,
                value
            ),
            ShapeKind.Map => MaterializeMap((IFunctionalMapSchema)resolved, value),
            _ => throw new NotSupportedException(
                $"CBOR codec does not support schema kind '{resolved.Kind}'."
            ),
        };
    }

    private static object MaterializeStructure(IFunctionalStructSchema schema, object value)
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
        }

        return schema.BuildObject(builder);
    }

    private static object MaterializeUnion(IFunctionalUnionSchema schema, object value)
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

    private static object MaterializeList(IFunctionalListSchema schema, object value)
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

    private static object MaterializeMap(IFunctionalMapSchema schema, object value)
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
            double doubleValue => DateTimeOffset.FromUnixTimeMilliseconds(
                (long)(doubleValue * 1000)
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
            return tag == CborTag.UnixTimeSeconds ? MaterializeTimestamp(inner!) : inner;
        }

        return state switch
        {
            CborReaderState.Null => ReadNull(ref reader),
            CborReaderState.Boolean => reader.ReadBoolean(),
            CborReaderState.UnsignedInteger => (long)reader.ReadUInt64(),
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
}
