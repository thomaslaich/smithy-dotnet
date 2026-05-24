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
        var unionSchema = GetUnionSchema(schema);
        if (unionSchema is not null)
        {
            WriteUnion(writer, unionSchema, value);
            return;
        }
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

    public void WriteByte(Schema schema, sbyte value) => writer.WriteNumberValue(value);

    public void WriteShort(Schema schema, short value) => writer.WriteNumberValue(value);

    public void WriteInteger(Schema schema, int value) => writer.WriteNumberValue(value);

    public void WriteLong(Schema schema, long value) => writer.WriteNumberValue(value);

    public void WriteFloat(Schema schema, float value)
    {
        if (float.IsNaN(value))
            writer.WriteStringValue("NaN");
        else if (float.IsPositiveInfinity(value))
            writer.WriteStringValue("Infinity");
        else if (float.IsNegativeInfinity(value))
            writer.WriteStringValue("-Infinity");
        else
            writer.WriteNumberValue(value);
    }

    public void WriteDouble(Schema schema, double value)
    {
        if (double.IsNaN(value))
            writer.WriteStringValue("NaN");
        else if (double.IsPositiveInfinity(value))
            writer.WriteStringValue("Infinity");
        else if (double.IsNegativeInfinity(value))
            writer.WriteStringValue("-Infinity");
        else
            writer.WriteNumberValue(value);
    }

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

    public void WriteTimestamp(Schema schema, DateTimeOffset value)
    {
        switch (GetTimestampFormat(schema))
        {
            case "epoch-seconds":
                writer.WriteRawValue(FormatEpochSeconds(value), skipInputValidation: true);
                break;
            case "http-date":
                writer.WriteStringValue(
                    value.ToUniversalTime().ToString("r", CultureInfo.InvariantCulture)
                );
                break;
            default:
                writer.WriteStringValue(FormatDateTime(value));
                break;
        }
    }

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
            var unionSchema = GetUnionSchema(schema);
            if (unionSchema is not null)
            {
                WriteUnion(writer, unionSchema, value);
                return;
            }
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

        public void WriteByte(Schema schema, sbyte value)
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
            if (float.IsNaN(value))
                writer.WriteStringValue("NaN");
            else if (float.IsPositiveInfinity(value))
                writer.WriteStringValue("Infinity");
            else if (float.IsNegativeInfinity(value))
                writer.WriteStringValue("-Infinity");
            else
                writer.WriteNumberValue(value);
        }

        public void WriteDouble(Schema schema, double value)
        {
            WritePropertyName(schema);
            if (double.IsNaN(value))
                writer.WriteStringValue("NaN");
            else if (double.IsPositiveInfinity(value))
                writer.WriteStringValue("Infinity");
            else if (double.IsNegativeInfinity(value))
                writer.WriteStringValue("-Infinity");
            else
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
            switch (GetTimestampFormat(schema))
            {
                case "epoch-seconds":
                    writer.WriteRawValue(FormatEpochSeconds(value), skipInputValidation: true);
                    break;
                case "http-date":
                    writer.WriteStringValue(
                        value.ToUniversalTime().ToString("r", CultureInfo.InvariantCulture)
                    );
                    break;
                default:
                    writer.WriteStringValue(FormatDateTime(value));
                    break;
            }
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

    private static void WriteUnion(Utf8JsonWriter writer, Schema schema, ISerializableStruct value)
    {
        var captured = UnionValueCaptureSerializer.Capture(value);
        var discriminatorName = GetDiscriminatorName(schema);
        if (discriminatorName is not null)
        {
            if (captured.Schema.HasTrait(JsonTraits.JsonUnknown))
            {
                captured.WriteValue(writer);
                return;
            }

            writer.WriteStartObject();
            writer.WriteString(discriminatorName, captured.Schema.MemberName);
            captured.WriteFlattenedObject(writer);
            writer.WriteEndObject();
            return;
        }

        if (captured.Schema.HasTrait(JsonTraits.JsonUnknown))
        {
            captured.WriteValue(writer);
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName(JsonNameResolver.Resolve(captured.Schema));
        captured.WriteValue(writer);
        writer.WriteEndObject();
    }

    private static string GetTimestampFormat(Schema schema)
    {
        var trait =
            schema.GetTrait(JsonTraits.TimestampFormat)
            ?? schema.Target?.GetTrait(JsonTraits.TimestampFormat);
        return trait?.Value.AsString() ?? "epoch-seconds";
    }

    private static string? GetDiscriminatorName(Schema schema)
    {
        return schema.GetTrait(JsonTraits.Discriminated)?.Value.AsString();
    }

    private static Schema? GetUnionSchema(Schema schema)
    {
        return schema.Kind == ShapeKind.Union ? schema
            : schema.Target?.Kind == ShapeKind.Union ? schema.Target
            : null;
    }

    private static string FormatDateTime(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var wholeSeconds = utc.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var fractionalTicks = utc.Ticks % TimeSpan.TicksPerSecond;
        if (fractionalTicks == 0)
        {
            return wholeSeconds + "Z";
        }

        var fractional = fractionalTicks.ToString("D7", CultureInfo.InvariantCulture).TrimEnd('0');
        return $"{wholeSeconds}.{fractional}Z";
    }

    private static string FormatEpochSeconds(DateTimeOffset value)
    {
        decimal seconds =
            (value.ToUniversalTime().Ticks - DateTimeOffset.UnixEpoch.Ticks)
            / (decimal)TimeSpan.TicksPerSecond;
        return seconds.ToString(CultureInfo.InvariantCulture);
    }

    private sealed class UnionValueCaptureSerializer : IShapeSerializer
    {
        public CapturedUnionMember? Captured { get; private set; }

        private UnionValueCaptureSerializer() { }

        public static CapturedUnionMember Capture(ISerializableStruct value)
        {
            var serializer = new UnionValueCaptureSerializer();
            value.SerializeMembers(serializer);
            return serializer.Captured
                ?? throw new InvalidOperationException("Union payload was empty.");
        }

        public void Dispose() { }

        public void Flush() { }

        public void WriteStruct(Schema schema, ISerializableStruct value)
        {
            Captured = CaptureValue(schema, serializer => serializer.WriteStruct(schema, value));
        }

        public void WriteList<TState>(
            Schema schema,
            TState state,
            int size,
            Action<TState, IShapeSerializer> consumer
        )
        {
            Captured = CaptureValue(
                schema,
                serializer => serializer.WriteList(schema, state, size, consumer)
            );
        }

        public void WriteMap<TState>(
            Schema schema,
            TState state,
            int size,
            Action<TState, IMapSerializer> consumer
        )
        {
            Captured = CaptureValue(
                schema,
                serializer => serializer.WriteMap(schema, state, size, consumer)
            );
        }

        public void WriteBoolean(Schema schema, bool value)
        {
            Captured = CaptureValue(schema, serializer => serializer.WriteBoolean(schema, value));
        }

        public void WriteByte(Schema schema, sbyte value)
        {
            Captured = CaptureValue(schema, serializer => serializer.WriteByte(schema, value));
        }

        public void WriteShort(Schema schema, short value)
        {
            Captured = CaptureValue(schema, serializer => serializer.WriteShort(schema, value));
        }

        public void WriteInteger(Schema schema, int value)
        {
            Captured = CaptureValue(schema, serializer => serializer.WriteInteger(schema, value));
        }

        public void WriteLong(Schema schema, long value)
        {
            Captured = CaptureValue(schema, serializer => serializer.WriteLong(schema, value));
        }

        public void WriteFloat(Schema schema, float value)
        {
            Captured = CaptureValue(schema, serializer => serializer.WriteFloat(schema, value));
        }

        public void WriteDouble(Schema schema, double value)
        {
            Captured = CaptureValue(schema, serializer => serializer.WriteDouble(schema, value));
        }

        public void WriteBigInteger(Schema schema, BigInteger value)
        {
            Captured = CaptureValue(
                schema,
                serializer => serializer.WriteBigInteger(schema, value)
            );
        }

        public void WriteBigDecimal(Schema schema, decimal value)
        {
            Captured = CaptureValue(
                schema,
                serializer => serializer.WriteBigDecimal(schema, value)
            );
        }

        public void WriteString(Schema schema, string value)
        {
            Captured = CaptureValue(schema, serializer => serializer.WriteString(schema, value));
        }

        public void WriteBlob(Schema schema, ReadOnlySpan<byte> value)
        {
            var bytes = value.ToArray();
            Captured = CaptureValue(schema, serializer => serializer.WriteBlob(schema, bytes));
        }

        public void WriteTimestamp(Schema schema, DateTimeOffset value)
        {
            Captured = CaptureValue(schema, serializer => serializer.WriteTimestamp(schema, value));
        }

        public void WriteDocument(Schema schema, Document value)
        {
            Captured = CaptureValue(schema, serializer => serializer.WriteDocument(schema, value));
        }

        public void WriteNull(Schema schema)
        {
            Captured = CaptureValue(schema, serializer => serializer.WriteNull(schema));
        }

        private static CapturedUnionMember CaptureValue(
            Schema schema,
            Action<JsonShapeSerializer> write
        )
        {
            using var buffer = new MemoryStream();
            using var serializer = new JsonShapeSerializer(buffer);
            write(serializer);
            serializer.Flush();
            buffer.Position = 0;
            using var document = JsonDocument.Parse(buffer.ToArray());
            return new CapturedUnionMember(schema, document.RootElement.Clone());
        }
    }

    private readonly record struct CapturedUnionMember(Schema Schema, JsonElement Value)
    {
        public void WriteValue(Utf8JsonWriter writer)
        {
            Value.WriteTo(writer);
        }

        public void WriteFlattenedObject(Utf8JsonWriter writer)
        {
            if (Value.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    $"Discriminated union member '{Schema.MemberName}' must serialize as a JSON object."
                );
            }

            foreach (var property in Value.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                property.Value.WriteTo(writer);
            }
        }
    }
}
