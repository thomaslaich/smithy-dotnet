using System.Globalization;
using System.Numerics;
using System.Text.Json;
using NSmithy.Core;
using NSmithy.Core.Serde;
using static NSmithy.Codecs.Json.JsonWire;

namespace NSmithy.Codecs.Json;

/// <summary>
/// Reads one JSON value, described by the plan of the member it belongs to. An aggregate read
/// walks the value and hands each member, element, entry, or case back to the schema.
/// </summary>
internal struct JsonShapeDeserializer(JsonElement current, JsonMemberPlan entry, WireReadMode mode)
    : IShapeDeserializer
{
    private readonly JsonElement current = current;
    private readonly JsonMemberPlan entry = entry;
    private readonly WireReadMode mode = mode;

    public readonly bool TryReadNull() =>
        current.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;

    public readonly bool ReadBoolean() =>
        ReadValue(current, "a boolean", static element => element.GetBoolean());

    public readonly sbyte ReadByte() =>
        ReadValue(current, "a byte", static element => element.GetSByte());

    public readonly short ReadShort() =>
        ReadValue(current, "a short", static element => element.GetInt16());

    public readonly int ReadInteger() =>
        ReadValue(current, "an integer", static element => element.GetInt32());

    public readonly long ReadLong() =>
        ReadValue(current, "a long", static element => element.GetInt64());

    public readonly float ReadFloat() => ReadValue(current, "a float", JsonWire.ReadFloat);

    public readonly double ReadDouble() => ReadValue(current, "a double", JsonWire.ReadDouble);

    public readonly BigInteger ReadBigInteger() =>
        ReadValue(
            current,
            "a bigInteger",
            static element => BigInteger.Parse(element.GetRawText(), CultureInfo.InvariantCulture)
        );

    public readonly decimal ReadBigDecimal() =>
        ReadValue(current, "a bigDecimal", static element => element.GetDecimal());

    public readonly string ReadString() =>
        TryReadNull()
            ? null!
            : ReadValue(current, "a string", static element => element.GetString()!);

    public readonly byte[] ReadBlob() =>
        ReadValue(current, "a base64-encoded blob", static element => element.GetBytesFromBase64());

    public readonly DateTimeOffset ReadTimestamp()
    {
        var format = entry.TimestampFormat;
        var readMode = mode;
        return ReadValue(
            current,
            $"a {format} timestamp",
            element => TimestampFormat.Read(element, format, readMode)
        );
    }

    public readonly Document ReadDocument() => Document.FromJsonElement(current);

    // An unmodeled value is not rejected here: enums stay open on the wire, and it is the
    // constraint validator that closes them on the server. Only a non-string is malformed.
    public readonly string ReadStringEnum() =>
        ReadValue(current, "a string", static element => element.GetString()!);

    public readonly int ReadIntEnum() =>
        ReadValue(current, "an integer", static element => element.GetInt32());

    public readonly Stream ReadStream() =>
        throw new NotSupportedException("JSON codec does not support streaming blob schemas.");

    public readonly IAsyncEnumerable<TEvent> ReadEventStream<TEvent>(Schema<TEvent> eventSchema) =>
        throw new NotSupportedException("JSON codec does not support event stream schemas.");

    public readonly T ReadStruct<T, TBuilder>(IStructSchema<T, TBuilder> schema)
    {
        var builder = schema.CreateTypedBuilder();
        ReadMembers(current, entry.Shape!, schema, builder, mode, projection: false);
        return schema.Build(builder);
    }

    /// <summary>
    /// Reads the members of a structure into <paramref name="builder"/>, in one pass over the
    /// object. An explicit null reads as an absent member, so a modeled default still applies.
    /// </summary>
    internal static void ReadMembers<T, TBuilder>(
        JsonElement value,
        JsonShapePlan plan,
        IStructSchema<T, TBuilder> schema,
        TBuilder builder,
        WireReadMode mode,
        bool projection
    )
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Malformed(value, "a JSON object");
        }

        var members = plan.Members;
        Span<bool> seen = members.Length <= 64 ? stackalloc bool[64] : new bool[members.Length];

        foreach (var property in value.EnumerateObject())
        {
            var index = plan.IndexOf(property);
            // First occurrence wins, as it does for a payload carrying a duplicate key.
            if (index < 0 || seen[index] || !plan.IsIncluded(index))
            {
                continue;
            }

            var member = members[index];
            if (property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                if (projection && member.IsRequired)
                {
                    throw new MissingRequiredMemberException(member.WireName);
                }

                continue;
            }

            seen[index] = true;
            var nested = new JsonShapeDeserializer(property.Value, member, mode);
            try
            {
                schema.DeserializeMember(builder, index, ref nested);
            }
            catch (MissingRequiredMemberException exception)
            {
                exception.PrependPathToken(member.WireName);
                throw;
            }
        }

        for (var index = 0; index < members.Length; index++)
        {
            if (seen[index] || !plan.IsIncluded(index))
            {
                continue;
            }

            var member = members[index];
            if (member.IsRequired)
            {
                throw new MissingRequiredMemberException(member.WireName);
            }

            if (member.Default is { } defaultValue)
            {
                var deserializer = new DocumentDeserializer(defaultValue);
                schema.DeserializeMember(builder, index, ref deserializer);
            }
        }
    }

    public readonly TCollection ReadList<TCollection, TElement, TBuilder>(
        IListSchema<TCollection, TElement, TBuilder> schema
    )
    {
        if (current.ValueKind != JsonValueKind.Array)
        {
            throw Malformed(current, "a JSON array");
        }

        var plan = entry.Shape!;
        var element = plan.Members[0];
        var builder = schema.CreateTypedBuilder();
        var index = 0;
        foreach (var item in current.EnumerateArray())
        {
            if (item.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
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

            var nested = new JsonShapeDeserializer(item, element, mode);
            try
            {
                schema.DeserializeElement(builder, ref nested);
            }
            catch (MissingRequiredMemberException exception)
            {
                exception.PrependPathToken(index.ToString(CultureInfo.InvariantCulture));
                throw;
            }

            index++;
        }

        return schema.Build(builder);
    }

    public readonly TDictionary ReadMap<TDictionary, TValue, TBuilder>(
        IMapSchema<TDictionary, TValue, TBuilder> schema
    )
    {
        if (current.ValueKind != JsonValueKind.Object)
        {
            throw Malformed(current, "a JSON object");
        }

        var plan = entry.Shape!;
        var valueEntry = plan.Members[1];
        var builder = schema.CreateTypedBuilder();
        foreach (var property in current.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                if (!plan.Sparse)
                {
                    throw MalformedRequestException.Serialization(
                        "A map that is not @sparse cannot contain null."
                    );
                }

                schema.Add(builder, property.Name, default!);
                continue;
            }

            var nested = new JsonShapeDeserializer(property.Value, valueEntry, mode);
            try
            {
                schema.DeserializeEntry(builder, property.Name, ref nested);
            }
            catch (MissingRequiredMemberException exception)
            {
                exception.PrependPathToken(property.Name);
                throw;
            }
        }

        return schema.Build(builder);
    }

    public readonly T ReadUnion<T>(IUnionSchema<T> schema)
    {
        if (current.ValueKind != JsonValueKind.Object)
        {
            throw Malformed(current, "a JSON object");
        }

        var plan = entry.Shape!;
        return plan.Discriminator is { } discriminator
            ? ReadDiscriminated(schema, plan, discriminator)
            : ReadSingleCase(schema, plan);
    }

    private readonly T ReadSingleCase<T>(IUnionSchema<T> schema, JsonShapePlan plan)
    {
        JsonProperty? found = null;
        var count = 0;
        foreach (var property in current.EnumerateObject())
        {
            if (property.NameEquals("__type"))
            {
                continue;
            }

            found = property;
            count++;
        }

        if (count != 1)
        {
            throw MalformedRequestException.Serialization(
                $"Expected a union with exactly one member set but found {count}."
            );
        }

        var index = plan.IndexOf(found!.Value);
        if (index >= 0 && index != plan.UnknownCase)
        {
            var nested = new JsonShapeDeserializer(found.Value.Value, plan.Members[index], mode);
            return schema.DeserializeCase(index, ref nested);
        }

        return plan.UnknownCase >= 0
            ? ReadUnknown(schema, plan)
            : throw MalformedRequestException.Serialization(
                $"Unknown union member '{found.Value.Name}'."
            );
    }

    private readonly T ReadDiscriminated<T>(
        IUnionSchema<T> schema,
        JsonShapePlan plan,
        string discriminator
    )
    {
        if (
            current.TryGetProperty(discriminator, out var tag)
            && tag.ValueKind == JsonValueKind.String
            && plan.IndexOf(tag.GetString()!) is var index and >= 0
            && index != plan.UnknownCase
        )
        {
            // The case's own value is the object without its tag, or the "value" property of a
            // case that is not a structure.
            var caseEntry = plan.Members[index];
            if (caseEntry.Shape?.Kind != ShapeKind.Structure)
            {
                var wrapped = current.TryGetProperty("value", out var inner) ? inner : default;
                var nested = new JsonShapeDeserializer(wrapped, caseEntry, mode);
                return schema.DeserializeCase(index, ref nested);
            }

            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                foreach (var property in current.EnumerateObject())
                {
                    if (!property.NameEquals(discriminator))
                    {
                        property.WriteTo(writer);
                    }
                }

                writer.WriteEndObject();
            }

            using var document = JsonDocument.Parse(buffer.ToArray());
            var untagged = new JsonShapeDeserializer(document.RootElement, caseEntry, mode);
            return schema.DeserializeCase(index, ref untagged);
        }

        return plan.UnknownCase >= 0
            ? ReadUnknown(schema, plan)
            : throw new InvalidOperationException(
                "Discriminated union is missing an unknown JSON case."
            );
    }

    // An open union's unknown case holds the whole value as a document.
    private readonly T ReadUnknown<T>(IUnionSchema<T> schema, JsonShapePlan plan)
    {
        var nested = new JsonShapeDeserializer(current, plan.Members[plan.UnknownCase], mode);
        return schema.DeserializeCase(plan.UnknownCase, ref nested);
    }
}
