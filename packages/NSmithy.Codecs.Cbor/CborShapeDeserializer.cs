using System.Formats.Cbor;
using System.Globalization;
using System.Numerics;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Cbor;

/// <summary>
/// Reads shapes from a pre-parsed CBOR value graph. The <c>current</c> cursor is mutated as
/// the visitor descends into containers.
/// </summary>
internal sealed class CborShapeDeserializer : IShapeDeserializer
{
    private object? current;

    private CborShapeDeserializer(object? value)
    {
        current = value;
    }

    public static CborShapeDeserializer Parse(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return new CborShapeDeserializer(null);
        }

        var reader = new CborReader(bytes, CborConformanceMode.Lax);
        var value = ReadValue(ref reader);
        return new CborShapeDeserializer(value);
    }

    public void Dispose() { }

    public bool IsNull() => current is null;

    public void ReadNull() { }

    public int ContainerSize()
    {
        return current switch
        {
            IReadOnlyList<object?> list => list.Count,
            IReadOnlyDictionary<string, object?> dict => dict.Count,
            _ => -1,
        };
    }

    public void ReadStruct<TState>(
        Schema schema,
        TState state,
        StructMemberConsumer<TState> consumer
    )
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(consumer.Member);

        if (current is not IReadOnlyDictionary<string, object?> map)
        {
            return;
        }

        var saved = current;
        try
        {
            if (schema.Kind == ShapeKind.Union)
            {
                // Unions are a single-key map
                foreach (var (key, value) in map)
                {
                    var memberSchema = schema.GetMember(key);
                    current = value;
                    if (memberSchema is null)
                    {
                        consumer.UnknownMember?.Invoke(state, key, this);
                    }
                    else
                    {
                        consumer.Member(state, memberSchema, this);
                    }

                    break; // only the first entry for unions
                }

                return;
            }

            foreach (var (key, value) in map)
            {
                var memberSchema = schema.GetMember(key);
                current = value;
                if (memberSchema is null)
                {
                    consumer.UnknownMember?.Invoke(state, key, this);
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

        if (current is not IReadOnlyList<object?> list)
        {
            return;
        }

        var saved = current;
        try
        {
            foreach (var item in list)
            {
                current = item;
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

        if (current is not IReadOnlyDictionary<string, object?> map)
        {
            return;
        }

        var saved = current;
        try
        {
            foreach (var (key, value) in map)
            {
                current = value;
                consumer(state, key, this);
            }
        }
        finally
        {
            current = saved;
        }
    }

    public bool ReadBoolean(Schema schema) =>
        Convert.ToBoolean(current, CultureInfo.InvariantCulture);

    public sbyte ReadByte(Schema schema) => Convert.ToSByte(current, CultureInfo.InvariantCulture);

    public short ReadShort(Schema schema) => Convert.ToInt16(current, CultureInfo.InvariantCulture);

    public int ReadInteger(Schema schema) => Convert.ToInt32(current, CultureInfo.InvariantCulture);

    public long ReadLong(Schema schema) => Convert.ToInt64(current, CultureInfo.InvariantCulture);

    public float ReadFloat(Schema schema) =>
        Convert.ToSingle(current, CultureInfo.InvariantCulture);

    public double ReadDouble(Schema schema) =>
        Convert.ToDouble(current, CultureInfo.InvariantCulture);

    public BigInteger ReadBigInteger(Schema schema) =>
        current switch
        {
            BigInteger bi => bi,
            _ => new BigInteger(Convert.ToInt64(current, CultureInfo.InvariantCulture)),
        };

    public decimal ReadBigDecimal(Schema schema) =>
        Convert.ToDecimal(current, CultureInfo.InvariantCulture);

    public string ReadString(Schema schema) =>
        (string)current! ?? throw new InvalidOperationException("Expected CBOR text string.");

    public byte[] ReadBlob(Schema schema) =>
        (byte[])current! ?? throw new InvalidOperationException("Expected CBOR byte string.");

    public DateTimeOffset ReadTimestamp(Schema schema)
    {
        return current switch
        {
            DateTimeOffset dto => dto,
            long l => DateTimeOffset.FromUnixTimeSeconds(l),
            double d => DateTimeOffset.FromUnixTimeMilliseconds((long)(d * 1000)),
            string s => DateTimeOffset.Parse(
                s,
                CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind
            ),
            _ => throw new InvalidOperationException(
                $"Cannot convert {current?.GetType().Name} to DateTimeOffset."
            ),
        };
    }

    public Document ReadDocument(Schema schema) =>
        throw new NotSupportedException("Smithy Document values are not supported by rpcv2Cbor.");

    // ── CBOR value parser ─────────────────────────────────────────────────────

    private static object? ReadValue(ref CborReader reader)
    {
        var state = reader.PeekState();

        if (state == CborReaderState.Tag)
        {
            var tag = reader.ReadTag();
            if (tag == CborTag.UnixTimeSeconds)
            {
                var inner = ReadValue(ref reader);
                return inner switch
                {
                    long l => DateTimeOffset.FromUnixTimeSeconds(l),
                    double d => DateTimeOffset.FromUnixTimeMilliseconds((long)(d * 1000)),
                    _ => inner,
                };
            }

            // Unknown tag: skip tag and read the wrapped value
            return ReadValue(ref reader);
        }

        return state switch
        {
            CborReaderState.Null => ReadNull(ref reader),
            CborReaderState.Boolean => reader.ReadBoolean(),
            CborReaderState.UnsignedInteger => (object)reader.ReadUInt64(),
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
            var key = reader.ReadTextString();
            var value = ReadValue(ref reader);
            dict[key] = value;
        }

        reader.ReadEndMap();
        return dict;
    }
}
