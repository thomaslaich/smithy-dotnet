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

    public float ReadFloat(Schema schema) => current.GetSingle();

    public double ReadDouble(Schema schema) => current.GetDouble();

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
        var trait = schema.GetTrait(JsonTraits.TimestampFormat);
        return trait?.Value.AsString() ?? "date-time";
    }

    private DateTimeOffset ReadEpochSecondsTimestamp()
    {
        decimal seconds =
            current.ValueKind switch
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

        long ticks = DateTimeOffset.UnixEpoch.Ticks + decimal.ToInt64(seconds * TimeSpan.TicksPerSecond);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
