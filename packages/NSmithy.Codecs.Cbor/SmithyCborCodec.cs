using System.Buffers;
using System.Formats.Cbor;
using System.Globalization;
using System.Numerics;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Cbor;

/// <summary>
/// CBOR codec that walks shapes via the <see cref="IShapeSerializer"/> /
/// <see cref="IShapeDeserializer"/> visitor interfaces. Follows the same pattern as
/// <c>SmithyJsonCodec</c>. No reflection; no annotation-based type resolution.
/// </summary>
public sealed class SmithyCborCodec : ISmithyCodec
{
    public static SmithyCborCodec Default { get; } = new();

    public string MediaType => "application/cbor";

    public IShapeSerializer CreateSerializer(Stream sink) => new CborShapeSerializer(sink);

    public IShapeDeserializer CreateDeserializer(ReadOnlySequence<byte> source) =>
        CborShapeDeserializer.Parse(source.ToArray());
}

// ─────────────────────────────────────────────────────────────────────────────
// Serializer
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Value-context CBOR serializer. Inside <see cref="WriteStruct"/> / <see cref="WriteMap{TState}"/>
/// it switches into a member-context where text-string keys are emitted before each value.
/// </summary>
internal sealed class CborShapeSerializer : IShapeSerializer
{
    internal readonly CborWriter writer;
    private readonly Stream? sink;

    /// <summary>Top-level: owns the CborWriter and flushes to a stream on Dispose.</summary>
    public CborShapeSerializer(Stream sink)
        : this(new CborWriter(CborConformanceMode.Lax))
    {
        this.sink = sink;
    }

    internal CborShapeSerializer(CborWriter writer)
    {
        this.writer = writer;
    }

    public void Dispose()
    {
        if (sink is not null)
        {
            Flush();
        }
    }

    public void Flush()
    {
        if (sink is not null)
        {
            var bytes = writer.Encode();
            sink.Write(bytes, 0, bytes.Length);
        }
    }

    public void WriteStruct(Schema schema, ISerializableStruct value)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartMap(null); // indefinite-length map
        var memberSerializer = new CborStructMemberSerializer(writer);
        value.SerializeMembers(memberSerializer);
        writer.WriteEndMap();
    }

    public void WriteList<TState>(
        Schema schema,
        TState state,
        int size,
        Action<TState, IShapeSerializer> consumer
    )
    {
        ArgumentNullException.ThrowIfNull(consumer);
        writer.WriteStartArray(size >= 0 ? size : null);
        consumer(state, this);
        writer.WriteEndArray();
    }

    public void WriteMap<TState>(
        Schema schema,
        TState state,
        int size,
        Action<TState, IMapSerializer> consumer
    )
    {
        ArgumentNullException.ThrowIfNull(consumer);
        writer.WriteStartMap(size >= 0 ? size : null);
        var mapSerializer = new CborMapSerializer(writer);
        consumer(state, mapSerializer);
        writer.WriteEndMap();
    }

    public void WriteBoolean(Schema schema, bool value) => writer.WriteBoolean(value);

    public void WriteByte(Schema schema, sbyte value) => writer.WriteInt32(value);

    public void WriteShort(Schema schema, short value) => writer.WriteInt32(value);

    public void WriteInteger(Schema schema, int value) => writer.WriteInt32(value);

    public void WriteLong(Schema schema, long value) => writer.WriteInt64(value);

    public void WriteFloat(Schema schema, float value) => writer.WriteSingle(value);

    public void WriteDouble(Schema schema, double value) => writer.WriteDouble(value);

    public void WriteBigInteger(Schema schema, BigInteger value) => writer.WriteInt64((long)value);

    public void WriteBigDecimal(Schema schema, decimal value) => writer.WriteDouble((double)value);

    public void WriteString(Schema schema, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteTextString(value);
    }

    public void WriteBlob(Schema schema, ReadOnlySpan<byte> value) => writer.WriteByteString(value);

    public void WriteTimestamp(Schema schema, DateTimeOffset value)
    {
        // CBOR tag 1 = epoch seconds
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

    public void WriteDocument(Schema schema, Document value) =>
        throw new NotSupportedException("Smithy Document values are not supported by rpcv2Cbor.");

    public void WriteNull(Schema schema) => writer.WriteNull();

    // ── member-context serializer ────────────────────────────────────────────

    /// <summary>
    /// Emits the member name as a CBOR text-string key immediately before each value.
    /// </summary>
    private sealed class CborStructMemberSerializer(CborWriter writer) : IShapeSerializer
    {
        public void Dispose() { }

        public void Flush() { }

        public void WriteStruct(Schema schema, ISerializableStruct value)
        {
            ArgumentNullException.ThrowIfNull(value);
            WriteKey(schema);
            writer.WriteStartMap(null);
            value.SerializeMembers(new CborStructMemberSerializer(writer));
            writer.WriteEndMap();
        }

        public void WriteList<TState>(
            Schema schema,
            TState state,
            int size,
            Action<TState, IShapeSerializer> consumer
        )
        {
            ArgumentNullException.ThrowIfNull(consumer);
            WriteKey(schema);
            writer.WriteStartArray(size >= 0 ? size : null);
            consumer(state, new CborShapeSerializer(writer));
            writer.WriteEndArray();
        }

        public void WriteMap<TState>(
            Schema schema,
            TState state,
            int size,
            Action<TState, IMapSerializer> consumer
        )
        {
            ArgumentNullException.ThrowIfNull(consumer);
            WriteKey(schema);
            writer.WriteStartMap(size >= 0 ? size : null);
            consumer(state, new CborMapSerializer(writer));
            writer.WriteEndMap();
        }

        public void WriteBoolean(Schema schema, bool value)
        {
            WriteKey(schema);
            writer.WriteBoolean(value);
        }

        public void WriteByte(Schema schema, sbyte value)
        {
            WriteKey(schema);
            writer.WriteInt32(value);
        }

        public void WriteShort(Schema schema, short value)
        {
            WriteKey(schema);
            writer.WriteInt32(value);
        }

        public void WriteInteger(Schema schema, int value)
        {
            WriteKey(schema);
            writer.WriteInt32(value);
        }

        public void WriteLong(Schema schema, long value)
        {
            WriteKey(schema);
            writer.WriteInt64(value);
        }

        public void WriteFloat(Schema schema, float value)
        {
            WriteKey(schema);
            writer.WriteSingle(value);
        }

        public void WriteDouble(Schema schema, double value)
        {
            WriteKey(schema);
            writer.WriteDouble(value);
        }

        public void WriteBigInteger(Schema schema, BigInteger value)
        {
            WriteKey(schema);
            writer.WriteInt64((long)value);
        }

        public void WriteBigDecimal(Schema schema, decimal value)
        {
            WriteKey(schema);
            writer.WriteDouble((double)value);
        }

        public void WriteString(Schema schema, string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            WriteKey(schema);
            writer.WriteTextString(value);
        }

        public void WriteBlob(Schema schema, ReadOnlySpan<byte> value)
        {
            WriteKey(schema);
            writer.WriteByteString(value);
        }

        public void WriteTimestamp(Schema schema, DateTimeOffset value)
        {
            WriteKey(schema);
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

        public void WriteDocument(Schema schema, Document value) =>
            throw new NotSupportedException(
                "Smithy Document values are not supported by rpcv2Cbor."
            );

        public void WriteNull(Schema schema)
        {
            WriteKey(schema);
            writer.WriteNull();
        }

        private void WriteKey(Schema memberSchema) =>
            writer.WriteTextString(memberSchema.MemberName!);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Map serializer
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class CborMapSerializer(CborWriter writer) : IMapSerializer
{
    public void Entry<TState>(
        string key,
        TState state,
        Action<TState, IShapeSerializer> valueWriter
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(valueWriter);
        writer.WriteTextString(key);
        var valueSerializer = new CborShapeSerializer(writer);
        valueWriter(state, valueSerializer);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Deserializer
// ─────────────────────────────────────────────────────────────────────────────

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
