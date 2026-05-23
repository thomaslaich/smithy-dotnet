using System.Globalization;
using System.Numerics;
using System.Text.Json;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Json;

/// <summary>
/// Reads shapes from a parsed <see cref="JsonDocument"/>. The cursor (<see cref="current"/>)
/// is mutated as the visitor descends into containers — consumers must read values inline
/// without retaining references to <c>this</c> across iterations.
/// </summary>
internal sealed class JsonShapeDeserializer : IShapeDeserializer
{
    private readonly JsonDocument document;
    private readonly bool ownsDocument;
    private JsonElement current;

    internal JsonShapeDeserializer(JsonDocument document, JsonElement root)
    {
        this.document = document;
        ownsDocument = true;
        current = root;
    }

    private JsonShapeDeserializer(JsonDocument document, JsonElement element, bool ownsDocument)
    {
        this.document = document;
        this.ownsDocument = ownsDocument;
        current = element;
    }

    public void Dispose()
    {
        if (ownsDocument)
        {
            document.Dispose();
        }
    }

    public bool IsNull() => current.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;

    public void ReadNull()
    {
        if (!IsNull())
        {
            throw new InvalidOperationException("Expected JSON null.");
        }
    }

    public int ContainerSize() =>
        current.ValueKind switch
        {
            JsonValueKind.Array => current.GetArrayLength(),
            JsonValueKind.Object => CountProperties(current),
            _ => -1,
        };

    public void ReadStruct<TState>(
        Schema schema,
        TState state,
        StructMemberConsumer<TState> consumer
    )
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(consumer.Member);
        EnsureKind(JsonValueKind.Object);
        if (schema.Kind == ShapeKind.Union)
        {
            ReadUnionStruct(schema, state, consumer);
            return;
        }
        var saved = current;
        try
        {
            foreach (var property in saved.EnumerateObject())
            {
                var memberSchema = ResolveMember(schema, property.Name);
                if (memberSchema is null)
                {
                    current = property.Value;
                    consumer.UnknownMember?.Invoke(state, property.Name, this);
                    continue;
                }

                current = property.Value;
                consumer.Member(state, memberSchema, this);
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
        EnsureKind(JsonValueKind.Array);
        var saved = current;
        try
        {
            foreach (var element in saved.EnumerateArray())
            {
                current = element;
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
        EnsureKind(JsonValueKind.Object);
        var saved = current;
        try
        {
            foreach (var property in saved.EnumerateObject())
            {
                current = property.Value;
                consumer(state, property.Name, this);
            }
        }
        finally
        {
            current = saved;
        }
    }

    public bool ReadBoolean(Schema schema) =>
        current.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw UnexpectedKind(JsonValueKind.True, JsonValueKind.False),
        };

    public sbyte ReadByte(Schema schema) => current.GetSByte();

    public short ReadShort(Schema schema) => current.GetInt16();

    public int ReadInteger(Schema schema) => current.GetInt32();

    public long ReadLong(Schema schema) => current.GetInt64();

    public float ReadFloat(Schema schema) =>
        current.ValueKind == JsonValueKind.String
            ? current.GetString() switch
            {
                "NaN" => float.NaN,
                "Infinity" => float.PositiveInfinity,
                "-Infinity" => float.NegativeInfinity,
                var s => float.Parse(s!, CultureInfo.InvariantCulture),
            }
            : current.GetSingle();

    public double ReadDouble(Schema schema) =>
        current.ValueKind == JsonValueKind.String
            ? current.GetString() switch
            {
                "NaN" => double.NaN,
                "Infinity" => double.PositiveInfinity,
                "-Infinity" => double.NegativeInfinity,
                var s => double.Parse(s!, CultureInfo.InvariantCulture),
            }
            : current.GetDouble();

    public BigInteger ReadBigInteger(Schema schema) =>
        BigInteger.Parse(current.GetRawText(), CultureInfo.InvariantCulture);

    public decimal ReadBigDecimal(Schema schema) => current.GetDecimal();

    public string ReadString(Schema schema) =>
        current.GetString()
        ?? throw new InvalidOperationException("Expected non-null JSON string.");

    public byte[] ReadBlob(Schema schema) => current.GetBytesFromBase64();

    public DateTimeOffset ReadTimestamp(Schema schema)
    {
        switch (GetTimestampFormat(schema))
        {
            case "epoch-seconds":
                return ReadEpochSecondsTimestamp();
            case "http-date":
                return DateTimeOffset.ParseExact(
                    ReadString(schema),
                    "r",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal
                );
            default:
                return DateTimeOffset.Parse(
                    ReadString(schema),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind
                );
        }
    }

    public Document ReadDocument(Schema schema) => Document.FromJsonElement(current);

    private static int CountProperties(JsonElement element)
    {
        var count = 0;
        foreach (var _ in element.EnumerateObject())
        {
            count++;
        }
        return count;
    }

    private static Schema? ResolveMember(Schema structSchema, string jsonName)
    {
        // First, look for a member whose jsonName trait matches.
        foreach (var member in structSchema.Members)
        {
            if (JsonNameResolver.Resolve(member) == jsonName)
            {
                return member;
            }
        }
        return null;
    }

    private void ReadUnionStruct<TState>(
        Schema schema,
        TState state,
        StructMemberConsumer<TState> consumer
    )
    {
        var saved = current;
        var jsonUnknownMember = schema.Members.FirstOrDefault(member =>
            member.HasTrait(JsonTraits.JsonUnknown)
        );
        var discriminatorName = GetDiscriminatorName(schema);

        if (discriminatorName is not null)
        {
            if (
                saved.TryGetProperty(discriminatorName, out var discriminator)
                && discriminator.ValueKind == JsonValueKind.String
            )
            {
                var memberSchema = schema.GetMember(discriminator.GetString()!);
                if (memberSchema is not null && !memberSchema.HasTrait(JsonTraits.JsonUnknown))
                {
                    using var payloadDocument = CreateObjectWithoutProperty(
                        saved,
                        discriminatorName
                    );
                    var previous = current;
                    try
                    {
                        current = payloadDocument.RootElement;
                        consumer.Member(state, memberSchema, this);
                    }
                    finally
                    {
                        current = previous;
                    }

                    return;
                }
            }

            if (jsonUnknownMember is not null)
            {
                var previous = current;
                try
                {
                    current = saved;
                    consumer.Member(state, jsonUnknownMember, this);
                }
                finally
                {
                    current = previous;
                }

                return;
            }
        }

        if (jsonUnknownMember is not null)
        {
            var properties = saved.EnumerateObject().ToArray();
            if (properties.Length == 1)
            {
                var memberSchema = ResolveMember(schema, properties[0].Name);
                if (memberSchema is not null && !memberSchema.HasTrait(JsonTraits.JsonUnknown))
                {
                    var previous = current;
                    try
                    {
                        current = properties[0].Value;
                        consumer.Member(state, memberSchema, this);
                    }
                    finally
                    {
                        current = previous;
                    }

                    return;
                }

                var old = current;
                try
                {
                    current = saved;
                    consumer.Member(state, jsonUnknownMember, this);
                }
                finally
                {
                    current = old;
                }

                return;
            }
        }

        foreach (var property in saved.EnumerateObject())
        {
            var memberSchema = ResolveMember(schema, property.Name);
            if (memberSchema is null)
            {
                current = property.Value;
                consumer.UnknownMember?.Invoke(state, property.Name, this);
                continue;
            }

            current = property.Value;
            consumer.Member(state, memberSchema, this);
        }
    }

    private void EnsureKind(JsonValueKind expected)
    {
        if (current.ValueKind != expected)
        {
            throw new InvalidOperationException(
                $"Expected JSON {expected} but found {current.ValueKind}."
            );
        }
    }

    private InvalidOperationException UnexpectedKind(params JsonValueKind[] expected) =>
        new($"Expected JSON {string.Join("/", expected)} but found {current.ValueKind}.");

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

    private static JsonDocument CreateObjectWithoutProperty(JsonElement value, string propertyName)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject())
            {
                if (property.NameEquals(propertyName))
                {
                    continue;
                }

                writer.WritePropertyName(property.Name);
                property.Value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return JsonDocument.Parse(buffer.ToArray());
    }

    private DateTimeOffset ReadEpochSecondsTimestamp()
    {
        decimal seconds = current.ValueKind switch
        {
            JsonValueKind.Number => decimal.Parse(
                current.GetRawText(),
                CultureInfo.InvariantCulture
            ),
            JsonValueKind.String => decimal.Parse(
                current.GetString()
                    ?? throw new InvalidOperationException("Expected non-null JSON string."),
                CultureInfo.InvariantCulture
            ),
            _ => throw UnexpectedKind(JsonValueKind.Number, JsonValueKind.String),
        };

        long ticks =
            DateTimeOffset.UnixEpoch.Ticks + decimal.ToInt64(seconds * TimeSpan.TicksPerSecond);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
