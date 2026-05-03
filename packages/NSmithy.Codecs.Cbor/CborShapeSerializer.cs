using System.Formats.Cbor;
using System.Numerics;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Cbor;

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
