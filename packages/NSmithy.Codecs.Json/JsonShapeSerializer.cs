using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Json;

/// <summary>Starts writing JSON for a top-level value or a projected structure.</summary>
internal static class JsonShapeSerializer
{
    public static JsonShapeSerializer<JsonRoot> ForRoot(
        Utf8JsonWriter writer,
        JsonMemberPlan root,
        bool materializeDefaults
    ) => new(writer, null, root, materializeDefaults);

    /// <summary>Writes <paramref name="value"/> as an object of the members <paramref name="plan"/> includes.</summary>
    public static void WriteObject<T>(
        Utf8JsonWriter writer,
        JsonShapePlan plan,
        bool materializeDefaults,
        IStructSchema<T> schema,
        T value
    )
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        var serializer = new JsonShapeSerializer<JsonStructure>(
            writer,
            plan,
            null,
            materializeDefaults
        );
        schema.SerializeMembers(value, ref serializer);
        writer.WriteEndObject();
    }
}

/// <summary>What a serializer writes into, fixed by its type so that every check on it compiles away.</summary>
internal interface IJsonContainer;

/// <summary>The top-level value.</summary>
internal readonly struct JsonRoot : IJsonContainer;

/// <summary>The members of a structure, each under its name.</summary>
internal readonly struct JsonStructure : IJsonContainer;

/// <summary>The case of a union, inside the object the union opens.</summary>
internal readonly struct JsonUnion : IJsonContainer;

/// <summary>The elements of a list, each a bare value.</summary>
internal readonly struct JsonList : IJsonContainer;

/// <summary>The entries of a map: a key that names the entry, then its value.</summary>
internal readonly struct JsonMap : IJsonContainer;

/// <summary>
/// Writes JSON for one aggregate, or for the top-level value. A nested aggregate gets its own
/// serializer positioned on that aggregate's plan, and typed by what kind of aggregate it is.
/// </summary>
internal readonly struct JsonShapeSerializer<TContainer> : IShapeSerializer
    where TContainer : struct, IJsonContainer
{
    private static readonly JsonEncodedText ValueName = JsonEncodedText.Encode("value");

    private readonly Utf8JsonWriter writer;
    private readonly JsonShapePlan? container;
    private readonly JsonMemberPlan? root;
    private readonly bool materializeDefaults;

    // Set while writing the members of a discriminated union's structure case inline: a member
    // that collides with the discriminator is the discriminator's to write.
    private readonly string? skipName;

    internal JsonShapeSerializer(
        Utf8JsonWriter writer,
        JsonShapePlan? container,
        JsonMemberPlan? root,
        bool materializeDefaults,
        string? skipName = null
    )
    {
        this.writer = writer;
        this.container = container;
        this.root = root;
        this.materializeDefaults = materializeDefaults;
        this.skipName = skipName;
    }

    private enum JsonSlot
    {
        Skip,
        Value,
        Named,
    }

    private JsonMemberPlan Entry(int member) =>
        typeof(TContainer) == typeof(JsonRoot) ? root! : container!.Members[member];

    /// <summary>
    /// Positions the writer for a value of <paramref name="member"/>: its property name inside a
    /// structure, its wrapping object inside a closed union. False means the member is not written.
    /// </summary>
    private bool Begin(int member)
    {
        switch (Slot(member, out var name))
        {
            case JsonSlot.Named:
                writer.WritePropertyName(name);
                return true;
            case JsonSlot.Value:
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Where a value of <paramref name="member"/> goes: under <paramref name="name"/> inside an
    /// object, as a bare value in an array or at the top level, or nowhere. A scalar writes its
    /// name and value in one call to the writer, which is cheaper than two.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private JsonSlot Slot(int member, out JsonEncodedText name)
    {
        name = default;
        if (typeof(TContainer) == typeof(JsonStructure))
        {
            if (!container!.IsIncluded(member))
            {
                return JsonSlot.Skip;
            }

            var entry = container.Members[member];
            if (skipName is not null && entry.WireName == skipName)
            {
                return JsonSlot.Skip;
            }

            name = entry.EncodedName;
            return JsonSlot.Named;
        }

        if (typeof(TContainer) == typeof(JsonUnion))
        {
            // Inside the object the union opens: the case's name, or for a discriminated union
            // the discriminator and then the case's value.
            if (container!.Discriminator is { } discriminator)
            {
                writer.WriteString(discriminator, container.Members[member].WireName);
                name = ValueName;
            }
            else
            {
                name = container.Members[member].EncodedName;
            }

            return JsonSlot.Named;
        }

        return JsonSlot.Value;
    }

    public bool WritesDefault(int member) =>
        materializeDefaults
        && typeof(TContainer) == typeof(JsonStructure)
        && container!.IsIncluded(member)
        && !container.Members[member].IsRequired;

    public void WriteNull(int member)
    {
        if (typeof(TContainer) == typeof(JsonStructure))
        {
            var entry = container!.Members[member];
            if (!entry.IsRequired)
            {
                return;
            }
        }

        if (typeof(TContainer) == typeof(JsonMap) && member == 0)
        {
            return;
        }

        if (Begin(member))
        {
            writer.WriteNullValue();
        }
    }

    public void WriteBoolean(int member, bool value)
    {
        switch (Slot(member, out var name))
        {
            case JsonSlot.Named:
                writer.WriteBoolean(name, value);
                break;
            case JsonSlot.Value:
                writer.WriteBooleanValue(value);
                break;
        }
    }

    public void WriteByte(int member, sbyte value)
    {
        switch (Slot(member, out var name))
        {
            case JsonSlot.Named:
                writer.WriteNumber(name, value);
                break;
            case JsonSlot.Value:
                writer.WriteNumberValue(value);
                break;
        }
    }

    public void WriteShort(int member, short value)
    {
        switch (Slot(member, out var name))
        {
            case JsonSlot.Named:
                writer.WriteNumber(name, value);
                break;
            case JsonSlot.Value:
                writer.WriteNumberValue(value);
                break;
        }
    }

    public void WriteInteger(int member, int value)
    {
        switch (Slot(member, out var name))
        {
            case JsonSlot.Named:
                writer.WriteNumber(name, value);
                break;
            case JsonSlot.Value:
                writer.WriteNumberValue(value);
                break;
        }
    }

    public void WriteLong(int member, long value)
    {
        switch (Slot(member, out var name))
        {
            case JsonSlot.Named:
                writer.WriteNumber(name, value);
                break;
            case JsonSlot.Value:
                writer.WriteNumberValue(value);
                break;
        }
    }

    public void WriteFloat(int member, float value)
    {
        if (Begin(member))
        {
            JsonWire.WriteFloat(writer, value);
        }
    }

    public void WriteDouble(int member, double value)
    {
        if (Begin(member))
        {
            JsonWire.WriteDouble(writer, value);
        }
    }

    public void WriteBigInteger(int member, BigInteger value)
    {
        var asString = Entry(member).BigNumbersAsStrings;
        if (Begin(member))
        {
            var text = value.ToString(CultureInfo.InvariantCulture);
            if (asString)
            {
                writer.WriteStringValue(text);
            }
            else
            {
                writer.WriteRawValue(text, true);
            }
        }
    }

    public void WriteBigDecimal(int member, decimal value)
    {
        var asString = Entry(member).BigNumbersAsStrings;
        if (Begin(member))
        {
            if (asString)
            {
                writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                writer.WriteNumberValue(value);
            }
        }
    }

    public void WriteString(int member, string value)
    {
        // A map's key is member 0: it names the entry rather than being a value of its own.
        if (typeof(TContainer) == typeof(JsonMap) && member == 0)
        {
            writer.WritePropertyName(value);
            return;
        }

        switch (Slot(member, out var name))
        {
            case JsonSlot.Named:
                writer.WriteString(name, value);
                break;
            case JsonSlot.Value:
                writer.WriteStringValue(value);
                break;
        }
    }

    public void WriteBlob(int member, byte[] value)
    {
        if (Begin(member))
        {
            writer.WriteBase64StringValue(value);
        }
    }

    public void WriteTimestamp(int member, DateTimeOffset value)
    {
        var format = Entry(member).TimestampFormat;
        if (Begin(member))
        {
            TimestampFormat.Write(writer, value, format);
        }
    }

    public void WriteDocument(int member, Document value)
    {
        // An open union's unknown case is the whole union value, written as it arrived.
        if (typeof(TContainer) == typeof(JsonUnion) && member == container!.UnknownCase)
        {
            DocumentJsonWriter.Write(writer, value);
            return;
        }

        if (Begin(member))
        {
            DocumentJsonWriter.Write(writer, value);
        }
    }

    public void WriteStringEnum(int member, string value) => WriteString(member, value);

    public void WriteIntEnum(int member, int value) => WriteInteger(member, value);

    // A member a projection excludes is skipped like any other; only one that would be written is
    // a stream this codec cannot encode.
    public void WriteStream(int member, Stream value)
    {
        if (!IsExcluded(member))
        {
            throw new NotSupportedException("JSON codec does not support streaming blob schemas.");
        }
    }

    public void WriteEventStream<TEvent>(
        int member,
        IAsyncEnumerable<TEvent> events,
        Schema<TEvent> eventSchema
    )
    {
        if (!IsExcluded(member))
        {
            throw new NotSupportedException("JSON codec does not support event stream schemas.");
        }
    }

    private bool IsExcluded(int member) =>
        typeof(TContainer) == typeof(JsonStructure) && !container!.IsIncluded(member);

    public void WriteStruct<T>(int member, T value, IStructSchema<T> schema)
    {
        var entry = Entry(member);
        if (
            typeof(TContainer) == typeof(JsonUnion)
            && container!.Discriminator is { } discriminator
        )
        {
            // A discriminated union's structure case is the structure itself, tagged with the
            // case name: {"type": "case", ...members}.
            writer.WriteStartObject();
            writer.WriteString(discriminator, entry.WireName);
            var inline = new JsonShapeSerializer<JsonStructure>(
                writer,
                entry.Shape!,
                null,
                true,
                discriminator
            );
            schema.SerializeMembers(value, ref inline);
            writer.WriteEndObject();
            return;
        }

        if (!Begin(member))
        {
            return;
        }

        writer.WriteStartObject();
        if (entry.Discriminator is { } leading)
        {
            writer.WriteString(leading.Key, leading.Value);
        }

        // Only the top-level structure follows the codec's default-materialization option;
        // nested structures always write their defaults.
        var nested = new JsonShapeSerializer<JsonStructure>(
            writer,
            entry.Shape!,
            null,
            typeof(TContainer) == typeof(JsonRoot) ? materializeDefaults : true
        );
        schema.SerializeMembers(value, ref nested);
        writer.WriteEndObject();
    }

    public void WriteList<TCollection, TElement>(
        int member,
        TCollection value,
        IListSchema<TCollection, TElement> schema
    )
    {
        var entry = Entry(member);
        if (!Begin(member))
        {
            return;
        }

        writer.WriteStartArray();
        var nested = new JsonShapeSerializer<JsonList>(writer, entry.Shape!, null, true);
        schema.SerializeElements(value, ref nested);
        writer.WriteEndArray();
    }

    public void WriteMap<TDictionary, TValue>(
        int member,
        TDictionary value,
        IMapSchema<TDictionary, TValue> schema
    )
    {
        var entry = Entry(member);
        if (!Begin(member))
        {
            return;
        }

        writer.WriteStartObject();
        var nested = new JsonShapeSerializer<JsonMap>(writer, entry.Shape!, null, true);
        schema.SerializeEntries(value, ref nested);
        writer.WriteEndObject();
    }

    public void WriteUnion<T>(int member, T value, UnionSchema<T> schema)
    {
        var entry = Entry(member);
        if (!Begin(member))
        {
            return;
        }

        // A closed union is {"case": value}, and a discriminated union {"type": "case", "value":
        // value}. Two cases write no wrapping object: a discriminated union's structure case is
        // the structure itself, tagged with the case name, and an open union's unknown case is
        // the union value as it arrived.
        var union = entry.Shape!;
        var index = schema.CaseOf(value);
        var wrapped =
            index != union.UnknownCase
            && !(
                union.Discriminator is not null
                && union.Members[index].Shape?.Kind == ShapeKind.Structure
            );
        if (wrapped)
        {
            writer.WriteStartObject();
        }

        var nested = new JsonShapeSerializer<JsonUnion>(writer, union, null, true);
        schema.SerializeCase(value, ref nested);
        if (wrapped)
        {
            writer.WriteEndObject();
        }
    }
}
