using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Json;

/// <summary>
/// Reads JSON straight off a <see cref="Utf8JsonReader"/>. One deserializer is the cursor for the
/// whole payload: a nested value is read through the same instance with <see cref="entry"/> set to
/// the plan of the member it belongs to. The reader is positioned on a value's first token and is
/// left on its last, so the caller moves on with one <see cref="Utf8JsonReader.Read"/>.
/// </summary>
internal ref struct JsonShapeDeserializer : IShapeDeserializer
{
    private Utf8JsonReader reader;
    private JsonMemberPlan entry;
    private readonly WireReadMode mode;

    private JsonShapeDeserializer(byte[] payload, JsonMemberPlan entry, WireReadMode mode)
    {
        reader = new Utf8JsonReader(payload);
        this.entry = entry;
        this.mode = mode;
    }

    /// <summary>Reads a whole payload, rejecting anything that is not one JSON value.</summary>
    public static T ReadPayload<T>(
        byte[] payload,
        Schema<T> schema,
        JsonMemberPlan root,
        WireReadMode mode
    )
    {
        var deserializer = new JsonShapeDeserializer(payload, root, mode);
        try
        {
            Start(ref deserializer.reader);
            var value = schema.Read(ref deserializer);
            End(ref deserializer.reader);
            return value;
        }
        catch (JsonException exception)
        {
            throw NotJson(exception);
        }
    }

    /// <summary>Reads a payload's members into <paramref name="builder"/>.</summary>
    public static void ReadPayloadMembers<T, TBuilder>(
        byte[] payload,
        JsonShapePlan plan,
        StructSchema<T, TBuilder> schema,
        TBuilder builder,
        WireReadMode mode
    )
    {
        var deserializer = new JsonShapeDeserializer(payload, null!, mode);
        try
        {
            Start(ref deserializer.reader);
            deserializer.ReadMembers(plan, schema, builder, projection: true);
            End(ref deserializer.reader);
        }
        catch (JsonException exception)
        {
            throw NotJson(exception);
        }
    }

    private static void Start(ref Utf8JsonReader reader)
    {
        if (!reader.Read())
        {
            throw new JsonException("The input does not contain any JSON tokens.");
        }
    }

    // Reading past the value is what makes the reader reject anything after it.
    private static void End(ref Utf8JsonReader reader)
    {
        while (reader.Read()) { }
    }

    private static MalformedRequestException NotJson(JsonException exception) =>
        MalformedRequestException.Serialization(
            $"Request body is not valid JSON: {exception.Message}"
        );

    public bool TryReadNull() => reader.TokenType == JsonTokenType.Null;

    public bool ReadBoolean()
    {
        try
        {
            return reader.GetBoolean();
        }
        catch (Exception exception) when (IsValueError(exception))
        {
            throw Malformed(ref reader, "a boolean");
        }
    }

    public sbyte ReadByte()
    {
        try
        {
            return reader.GetSByte();
        }
        catch (Exception exception) when (IsValueError(exception))
        {
            throw Malformed(ref reader, "a byte");
        }
    }

    public short ReadShort()
    {
        try
        {
            return reader.GetInt16();
        }
        catch (Exception exception) when (IsValueError(exception))
        {
            throw Malformed(ref reader, "a short");
        }
    }

    public int ReadInteger()
    {
        try
        {
            return reader.GetInt32();
        }
        catch (Exception exception) when (IsValueError(exception))
        {
            throw Malformed(ref reader, "an integer");
        }
    }

    public long ReadLong()
    {
        try
        {
            return reader.GetInt64();
        }
        catch (Exception exception) when (IsValueError(exception))
        {
            throw Malformed(ref reader, "a long");
        }
    }

    public float ReadFloat()
    {
        try
        {
            return reader.TokenType == JsonTokenType.String
                ? reader.GetString() switch
                {
                    "NaN" => float.NaN,
                    "Infinity" => float.PositiveInfinity,
                    "-Infinity" => float.NegativeInfinity,
                    var s => throw new FormatException($"'{s}' is not a float."),
                }
                : reader.GetSingle();
        }
        catch (Exception exception) when (IsValueError(exception))
        {
            throw Malformed(ref reader, "a float");
        }
    }

    public double ReadDouble()
    {
        try
        {
            return reader.TokenType == JsonTokenType.String
                ? reader.GetString() switch
                {
                    "NaN" => double.NaN,
                    "Infinity" => double.PositiveInfinity,
                    "-Infinity" => double.NegativeInfinity,
                    var s => throw new FormatException($"'{s}' is not a double."),
                }
                : reader.GetDouble();
        }
        catch (Exception exception) when (IsValueError(exception))
        {
            throw Malformed(ref reader, "a double");
        }
    }

    public BigInteger ReadBigInteger()
    {
        var asString = entry.BigNumbersAsStrings;
        try
        {
            return asString
                    ? BigInteger.Parse(
                        reader.GetString()!,
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture
                    )
                : reader.TokenType == JsonTokenType.Number
                    ? BigInteger.Parse(
                        Encoding.UTF8.GetString(reader.ValueSpan),
                        CultureInfo.InvariantCulture
                    )
                : throw new FormatException("Not a number.");
        }
        catch (Exception exception) when (IsValueError(exception))
        {
            throw Malformed(ref reader, asString ? "a bigInteger string" : "a bigInteger");
        }
    }

    public decimal ReadBigDecimal()
    {
        var asString = entry.BigNumbersAsStrings;
        try
        {
            return asString
                ? decimal.Parse(
                    reader.GetString()!,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture
                )
                : reader.GetDecimal();
        }
        catch (Exception exception) when (IsValueError(exception))
        {
            throw Malformed(ref reader, asString ? "a bigDecimal string" : "a bigDecimal");
        }
    }

    public string ReadString()
    {
        if (TryReadNull())
        {
            return null!;
        }

        try
        {
            return reader.GetString()!;
        }
        catch (Exception exception) when (IsValueError(exception))
        {
            throw Malformed(ref reader, "a string");
        }
    }

    public byte[] ReadBlob()
    {
        try
        {
            return reader.GetBytesFromBase64();
        }
        catch (Exception exception) when (IsValueError(exception))
        {
            throw Malformed(ref reader, "a base64-encoded blob");
        }
    }

    public DateTimeOffset ReadTimestamp()
    {
        var format = entry.TimestampFormat;
        try
        {
            return format switch
            {
                "epoch-seconds" => DateTimeOffset.FromUnixTimeMilliseconds(
                    (long)(reader.GetDouble() * 1000)
                ),
                "http-date" => DateTimeOffset.ParseExact(
                    reader.GetString()!,
                    "r",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None
                ),
                _ => Rfc3339.Parse(reader.GetString()!, mode),
            };
        }
        catch (Exception exception) when (IsValueError(exception))
        {
            throw Malformed(ref reader, $"a {format} timestamp");
        }
    }

    public Document ReadDocument() => Document.FromJsonElement(JsonElement.ParseValue(ref reader));

    // An unmodeled value is not rejected here: enums stay open on the wire, and it is the
    // constraint validator that closes them on the server. Only a non-string is malformed.
    public string ReadStringEnum()
    {
        try
        {
            return reader.TokenType == JsonTokenType.String
                ? reader.GetString()!
                : throw new InvalidOperationException();
        }
        catch (Exception exception) when (IsValueError(exception))
        {
            throw Malformed(ref reader, "a string");
        }
    }

    public int ReadIntEnum()
    {
        try
        {
            return reader.GetInt32();
        }
        catch (Exception exception) when (IsValueError(exception))
        {
            throw Malformed(ref reader, "an integer");
        }
    }

    public Stream ReadStream() =>
        throw new NotSupportedException("JSON codec does not support streaming blob schemas.");

    public IAsyncEnumerable<TEvent> ReadEventStream<TEvent>(Schema<TEvent> eventSchema) =>
        throw new NotSupportedException("JSON codec does not support event stream schemas.");

    public T ReadStruct<T, TBuilder>(StructSchema<T, TBuilder> schema)
    {
        var builder = schema.CreateTypedBuilder();
        ReadMembers(entry.Shape!, schema, builder, projection: false);
        return schema.Build(builder);
    }

    /// <summary>
    /// Reads the members of a structure into <paramref name="builder"/>, in one pass over the
    /// object. An explicit null reads as an absent member, so a modeled default still applies.
    /// </summary>
    private void ReadMembers<T, TBuilder>(
        JsonShapePlan plan,
        StructSchema<T, TBuilder> schema,
        TBuilder builder,
        bool projection
    )
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw Malformed(ref reader, "a JSON object");
        }

        var members = plan.Members;
        Span<bool> seen = members.Length <= 64 ? stackalloc bool[64] : new bool[members.Length];

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var index = plan.IndexOf(ref reader);
            reader.Read();

            // First occurrence wins, as it does for a payload carrying a duplicate key.
            if (index < 0 || seen[index] || !plan.IsIncluded(index))
            {
                reader.Skip();
                continue;
            }

            var member = members[index];
            if (reader.TokenType == JsonTokenType.Null)
            {
                if (projection && member.IsRequired)
                {
                    throw new MissingRequiredMemberException(member.WireName);
                }

                continue;
            }

            seen[index] = true;
            var outer = entry;
            entry = member;
            try
            {
                schema.DeserializeMember(builder, index, ref this);
            }
            catch (MissingRequiredMemberException exception)
            {
                exception.PrependPathToken(member.WireName);
                throw;
            }

            entry = outer;
        }

        for (var index = 0; index < members.Length; index++)
        {
            if (seen[index] || !plan.IsIncluded(index))
            {
                continue;
            }

            // An absent member keeps the value its builder started with: its modeled default, or
            // none.
            if (members[index].IsRequired)
            {
                throw new MissingRequiredMemberException(members[index].WireName);
            }
        }
    }

    public TCollection ReadList<TCollection, TElement, TBuilder>(
        ListSchema<TCollection, TElement, TBuilder> schema
    )
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw Malformed(ref reader, "a JSON array");
        }

        var plan = entry.Shape!;
        var element = plan.Members[0];
        var builder = schema.CreateTypedBuilder();
        var index = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                if (!plan.Sparse)
                {
                    throw MalformedRequestException.Serialization(
                        "A list that is not @sparse cannot contain null."
                    );
                }

                schema.Add(builder, default!);
                index++;
                continue;
            }

            var outer = entry;
            entry = element;
            try
            {
                schema.DeserializeElement(builder, ref this);
            }
            catch (MissingRequiredMemberException exception)
            {
                exception.PrependPathToken(index.ToString(CultureInfo.InvariantCulture));
                throw;
            }

            entry = outer;

            index++;
        }

        return schema.Build(builder);
    }

    public TDictionary ReadMap<TDictionary, TValue, TBuilder>(
        MapSchema<TDictionary, TValue, TBuilder> schema
    )
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw Malformed(ref reader, "a JSON object");
        }

        var plan = entry.Shape!;
        var valueEntry = plan.Members[1];
        var builder = schema.CreateTypedBuilder();
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var key = reader.GetString()!;
            reader.Read();
            if (reader.TokenType == JsonTokenType.Null)
            {
                if (!plan.Sparse)
                {
                    throw MalformedRequestException.Serialization(
                        "A map that is not @sparse cannot contain null."
                    );
                }

                schema.Add(builder, key, default!);
                continue;
            }

            var outer = entry;
            entry = valueEntry;
            try
            {
                schema.DeserializeEntry(builder, key, ref this);
            }
            catch (MissingRequiredMemberException exception)
            {
                exception.PrependPathToken(key);
                throw;
            }

            entry = outer;
        }

        return schema.Build(builder);
    }

    public T ReadUnion<T>(UnionSchema<T> schema)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw Malformed(ref reader, "a JSON object");
        }

        var plan = entry.Shape!;
        return plan.Discriminator is not null
            ? ReadDiscriminated(schema, plan)
            : ReadSingleCase(schema, plan);
    }

    private T ReadSingleCase<T>(UnionSchema<T> schema, JsonShapePlan plan)
    {
        // A copy of the reader looks ahead without moving this one: the object must hold exactly
        // one case before any of it is read.
        var scan = reader;
        var count = 0;
        var index = -1;
        string? name = null;
        while (scan.Read() && scan.TokenType == JsonTokenType.PropertyName)
        {
            if (!scan.ValueTextEquals("__type"u8))
            {
                index = plan.IndexOf(ref scan);
                name = scan.GetString();
                count++;
            }

            scan.Read();
            scan.Skip();
        }

        if (count != 1)
        {
            throw MalformedRequestException.Serialization(
                $"Expected a union with exactly one member set but found {count}."
            );
        }

        if (index < 0 || index == plan.UnknownCase)
        {
            return plan.UnknownCase >= 0
                ? ReadUnknown(schema, plan)
                : throw MalformedRequestException.Serialization($"Unknown union member '{name}'.");
        }

        T value = default!;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var isCase = !reader.ValueTextEquals("__type"u8);
            reader.Read();
            if (isCase)
            {
                value = ReadCase(schema, index, plan.Members[index]);
            }
            else
            {
                reader.Skip();
            }
        }

        return value;
    }

    private T ReadDiscriminated<T>(UnionSchema<T> schema, JsonShapePlan plan)
    {
        var tagName = plan.Utf8Discriminator!;
        var scan = reader;
        string? tag = null;
        while (scan.Read() && scan.TokenType == JsonTokenType.PropertyName)
        {
            var isTag = tag is null && scan.ValueTextEquals(tagName);
            scan.Read();
            if (isTag && scan.TokenType == JsonTokenType.String)
            {
                tag = scan.GetString();
                break;
            }

            scan.Skip();
        }

        if (
            tag is null
            || plan.IndexOf(tag) is not (var index and >= 0)
            || index == plan.UnknownCase
        )
        {
            return plan.UnknownCase >= 0
                ? ReadUnknown(schema, plan)
                : throw new InvalidOperationException(
                    "Discriminated union is missing an unknown JSON case."
                );
        }

        // A structure case is the object itself, which skips the tag as a property it does not
        // model (alloy forbids a case structure from having a member of that name). Any other case is the
        // object's "value" property.
        var caseEntry = plan.Members[index];
        if (caseEntry.Shape?.Kind == ShapeKind.Structure)
        {
            return ReadCase(schema, index, caseEntry);
        }

        T value = default!;
        var found = false;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var isValue = !found && reader.ValueTextEquals("value"u8);
            reader.Read();
            if (isValue)
            {
                value = ReadCase(schema, index, caseEntry);
                found = true;
            }
            else
            {
                reader.Skip();
            }
        }

        return found
            ? value
            : throw MalformedRequestException.Serialization(
                $"Expected a 'value' property for union case '{caseEntry.WireName}'."
            );
    }

    // An open union's unknown case holds the whole value as a document.
    private T ReadUnknown<T>(UnionSchema<T> schema, JsonShapePlan plan) =>
        ReadCase(schema, plan.UnknownCase, plan.Members[plan.UnknownCase]);

    private T ReadCase<T>(UnionSchema<T> schema, int index, JsonMemberPlan caseEntry)
    {
        var outer = entry;
        entry = caseEntry;
        var value = schema.DeserializeCase(index, ref this);
        entry = outer;
        return value;
    }

    private static bool IsValueError(Exception exception) =>
        exception
            is FormatException
                or InvalidOperationException
                or OverflowException
                or ArgumentException;

    private static MalformedRequestException Malformed(ref Utf8JsonReader reader, string expected)
    {
        const int limit = 64;
        var found = reader.TokenType switch
        {
            JsonTokenType.StartObject => "an object",
            JsonTokenType.StartArray => "an array",
            JsonTokenType.String => $"\"{Encoding.UTF8.GetString(reader.ValueSpan)}\"",
            _ => Encoding.UTF8.GetString(reader.ValueSpan),
        };
        if (found.Length > limit)
        {
            found = string.Concat(found.AsSpan(0, limit), "…");
        }

        return MalformedRequestException.Serialization($"Expected {expected} but found {found}.");
    }
}
