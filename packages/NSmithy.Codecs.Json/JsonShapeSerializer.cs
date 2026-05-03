using System.Globalization;
using System.Numerics;
using System.Text.Json;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Json;

/// <summary>
/// Value-context shape serializer over a <see cref="Utf8JsonWriter"/>. Inside
/// <see cref="WriteStruct"/> / <see cref="WriteMap{TState}"/> it switches into a member-context
/// where property names are emitted before each value.
/// </summary>
internal sealed class JsonShapeSerializer : IShapeSerializer
{
    private readonly Utf8JsonWriter writer;
    private readonly bool ownsWriter;

    public JsonShapeSerializer(Stream sink)
        : this(new Utf8JsonWriter(sink), ownsWriter: true) { }

    internal JsonShapeSerializer(Utf8JsonWriter writer, bool ownsWriter)
    {
        this.writer = writer;
        this.ownsWriter = ownsWriter;
    }

    public void Dispose()
    {
        if (ownsWriter)
        {
            writer.Dispose();
        }
    }

    public void Flush() => writer.Flush();

    public void WriteStruct(Schema schema, ISerializableStruct value)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        var memberSerializer = new JsonStructMemberSerializer(writer);
        value.SerializeMembers(memberSerializer);
        writer.WriteEndObject();
    }

    public void WriteList<TState>(
        Schema schema,
        TState state,
        int size,
        Action<TState, IShapeSerializer> consumer
    )
    {
        ArgumentNullException.ThrowIfNull(consumer);
        writer.WriteStartArray();
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
        writer.WriteStartObject();
        var mapSerializer = new JsonMapSerializer(writer);
        consumer(state, mapSerializer);
        writer.WriteEndObject();
    }

    public void WriteBoolean(Schema schema, bool value) => writer.WriteBooleanValue(value);

    public void WriteByte(Schema schema, byte value) => writer.WriteNumberValue(value);

    public void WriteShort(Schema schema, short value) => writer.WriteNumberValue(value);

    public void WriteInteger(Schema schema, int value) => writer.WriteNumberValue(value);

    public void WriteLong(Schema schema, long value) => writer.WriteNumberValue(value);

    public void WriteFloat(Schema schema, float value) => writer.WriteNumberValue(value);

    public void WriteDouble(Schema schema, double value) => writer.WriteNumberValue(value);

    public void WriteBigInteger(Schema schema, BigInteger value) =>
        writer.WriteRawValue(
            value.ToString(CultureInfo.InvariantCulture),
            skipInputValidation: true
        );

    public void WriteBigDecimal(Schema schema, decimal value) => writer.WriteNumberValue(value);

    public void WriteString(Schema schema, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStringValue(value);
    }

    public void WriteBlob(Schema schema, ReadOnlySpan<byte> value) =>
        writer.WriteBase64StringValue(value);

    public void WriteTimestamp(Schema schema, DateTimeOffset value) =>
        writer.WriteStringValue(value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));

    public void WriteDocument(Schema schema, Document value) =>
        DocumentJsonWriter.Write(writer, value);

    public void WriteNull(Schema schema) => writer.WriteNullValue();

    /// <summary>
    /// Member-context serializer used inside structures. Every <c>Write*</c> emits the member
    /// name (resolved via <see cref="JsonNameResolver"/>) immediately before the value.
    /// </summary>
    private sealed class JsonStructMemberSerializer(Utf8JsonWriter writer) : IShapeSerializer
    {
        public void Dispose() { }

        public void Flush() => writer.Flush();

        public void WriteStruct(Schema schema, ISerializableStruct value)
        {
            ArgumentNullException.ThrowIfNull(value);
            WritePropertyName(schema);
            writer.WriteStartObject();
            value.SerializeMembers(new JsonStructMemberSerializer(writer));
            writer.WriteEndObject();
        }

        public void WriteList<TState>(
            Schema schema,
            TState state,
            int size,
            Action<TState, IShapeSerializer> consumer
        )
        {
            ArgumentNullException.ThrowIfNull(consumer);
            WritePropertyName(schema);
            writer.WriteStartArray();
            consumer(state, new JsonShapeSerializer(writer, ownsWriter: false));
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
            WritePropertyName(schema);
            writer.WriteStartObject();
            consumer(state, new JsonMapSerializer(writer));
            writer.WriteEndObject();
        }

        public void WriteBoolean(Schema schema, bool value)
        {
            WritePropertyName(schema);
            writer.WriteBooleanValue(value);
        }

        public void WriteByte(Schema schema, byte value)
        {
            WritePropertyName(schema);
            writer.WriteNumberValue(value);
        }

        public void WriteShort(Schema schema, short value)
        {
            WritePropertyName(schema);
            writer.WriteNumberValue(value);
        }

        public void WriteInteger(Schema schema, int value)
        {
            WritePropertyName(schema);
            writer.WriteNumberValue(value);
        }

        public void WriteLong(Schema schema, long value)
        {
            WritePropertyName(schema);
            writer.WriteNumberValue(value);
        }

        public void WriteFloat(Schema schema, float value)
        {
            WritePropertyName(schema);
            writer.WriteNumberValue(value);
        }

        public void WriteDouble(Schema schema, double value)
        {
            WritePropertyName(schema);
            writer.WriteNumberValue(value);
        }

        public void WriteBigInteger(Schema schema, BigInteger value)
        {
            WritePropertyName(schema);
            writer.WriteRawValue(
                value.ToString(CultureInfo.InvariantCulture),
                skipInputValidation: true
            );
        }

        public void WriteBigDecimal(Schema schema, decimal value)
        {
            WritePropertyName(schema);
            writer.WriteNumberValue(value);
        }

        public void WriteString(Schema schema, string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            WritePropertyName(schema);
            writer.WriteStringValue(value);
        }

        public void WriteBlob(Schema schema, ReadOnlySpan<byte> value)
        {
            WritePropertyName(schema);
            writer.WriteBase64StringValue(value);
        }

        public void WriteTimestamp(Schema schema, DateTimeOffset value)
        {
            WritePropertyName(schema);
            writer.WriteStringValue(value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        }

        public void WriteDocument(Schema schema, Document value)
        {
            WritePropertyName(schema);
            DocumentJsonWriter.Write(writer, value);
        }

        public void WriteNull(Schema schema)
        {
            WritePropertyName(schema);
            writer.WriteNullValue();
        }

        private void WritePropertyName(Schema memberSchema) =>
            writer.WritePropertyName(JsonNameResolver.Resolve(memberSchema));
    }
}
