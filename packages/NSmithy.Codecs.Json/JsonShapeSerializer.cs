using System.Globalization;
using System.Numerics;
using System.Text.Json;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Json;

/// <summary>
/// Writes JSON for one aggregate, or for the top-level value. A nested aggregate gets its own
/// serializer positioned on that aggregate's plan.
/// </summary>
internal struct JsonShapeSerializer : IShapeSerializer
{
    private readonly Utf8JsonWriter writer;
    private readonly JsonShapePlan? container;
    private readonly JsonMemberPlan? root;
    private readonly bool materializeDefaults;

    // Set while writing the members of a discriminated union's structure case inline: a member
    // that collides with the discriminator is the discriminator's to write.
    private readonly string? skipName;

    public JsonShapeSerializer(Utf8JsonWriter writer, JsonMemberPlan root, bool materializeDefaults)
    {
        this.writer = writer;
        this.root = root;
        this.materializeDefaults = materializeDefaults;
    }

    private JsonShapeSerializer(
        Utf8JsonWriter writer,
        JsonShapePlan container,
        bool materializeDefaults,
        string? skipName = null
    )
    {
        this.writer = writer;
        this.container = container;
        this.materializeDefaults = materializeDefaults;
        this.skipName = skipName;
    }

    /// <summary>Writes <paramref name="value"/> as an object of the members <paramref name="plan"/> includes.</summary>
    internal static void WriteObject<T>(
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
        var serializer = new JsonShapeSerializer(writer, plan, materializeDefaults);
        schema.SerializeMembers(value, ref serializer);
        writer.WriteEndObject();
    }

    private readonly JsonMemberPlan Entry(int member) =>
        member == MemberIndex.Root ? root! : container!.Members[member];

    /// <summary>
    /// Positions the writer for a value of <paramref name="member"/>: its property name inside a
    /// structure, its wrapping object inside a closed union. False means the member is not written.
    /// </summary>
    private readonly bool Begin(int member)
    {
        if (container is null)
        {
            return true;
        }

        switch (container.Kind)
        {
            case ShapeKind.Structure:
                if (!container.IsIncluded(member))
                {
                    return false;
                }

                var entry = container.Members[member];
                if (skipName is not null && entry.WireName == skipName)
                {
                    return false;
                }

                writer.WritePropertyName(entry.EncodedName);
                return true;
            case ShapeKind.Union:
                // A closed union is {"case": value}; a discriminated union that is not a
                // structure case is {"type": "case", "value": value}.
                writer.WriteStartObject();
                if (container.Discriminator is { } discriminator)
                {
                    writer.WriteString(discriminator, container.Members[member].WireName);
                    writer.WritePropertyName("value");
                }
                else
                {
                    writer.WritePropertyName(container.Members[member].EncodedName);
                }

                return true;
            default:
                return true;
        }
    }

    private readonly void End()
    {
        if (container?.Kind == ShapeKind.Union)
        {
            writer.WriteEndObject();
        }
    }

    public readonly bool WritesDefault(int member) =>
        materializeDefaults
        && container?.Kind == ShapeKind.Structure
        && container.IsIncluded(member)
        && !container.Members[member].IsRequired;

    public void WriteNull(int member)
    {
        if (container?.Kind == ShapeKind.Structure)
        {
            var entry = container.Members[member];
            if (!entry.IsRequired)
            {
                return;
            }
        }

        if (container?.Kind == ShapeKind.Map && member == 0)
        {
            return;
        }

        if (Begin(member))
        {
            writer.WriteNullValue();
            End();
        }
    }

    public void WriteBoolean(int member, bool value)
    {
        if (Begin(member))
        {
            writer.WriteBooleanValue(value);
            End();
        }
    }

    public void WriteByte(int member, sbyte value)
    {
        if (Begin(member))
        {
            writer.WriteNumberValue(value);
            End();
        }
    }

    public void WriteShort(int member, short value)
    {
        if (Begin(member))
        {
            writer.WriteNumberValue(value);
            End();
        }
    }

    public void WriteInteger(int member, int value)
    {
        if (Begin(member))
        {
            writer.WriteNumberValue(value);
            End();
        }
    }

    public void WriteLong(int member, long value)
    {
        if (Begin(member))
        {
            writer.WriteNumberValue(value);
            End();
        }
    }

    public void WriteFloat(int member, float value)
    {
        if (Begin(member))
        {
            JsonWire.WriteFloat(writer, value);
            End();
        }
    }

    public void WriteDouble(int member, double value)
    {
        if (Begin(member))
        {
            JsonWire.WriteDouble(writer, value);
            End();
        }
    }

    public void WriteBigInteger(int member, BigInteger value)
    {
        if (Begin(member))
        {
            writer.WriteRawValue(value.ToString(CultureInfo.InvariantCulture), true);
            End();
        }
    }

    public void WriteBigDecimal(int member, decimal value)
    {
        if (Begin(member))
        {
            writer.WriteNumberValue(value);
            End();
        }
    }

    public void WriteString(int member, string value)
    {
        // A map's key is member 0: it names the entry rather than being a value of its own.
        if (container?.Kind == ShapeKind.Map && member == 0)
        {
            writer.WritePropertyName(value);
            return;
        }

        if (Begin(member))
        {
            writer.WriteStringValue(value);
            End();
        }
    }

    public void WriteBlob(int member, byte[] value)
    {
        if (Begin(member))
        {
            writer.WriteBase64StringValue(value);
            End();
        }
    }

    public void WriteTimestamp(int member, DateTimeOffset value)
    {
        var format = Entry(member).TimestampFormat;
        if (Begin(member))
        {
            TimestampFormat.Write(writer, value, format);
            End();
        }
    }

    public void WriteDocument(int member, Document value)
    {
        // An open union's unknown case is the whole union value, written as it arrived.
        if (container?.Kind == ShapeKind.Union && member == container.UnknownCase)
        {
            DocumentJsonWriter.Write(writer, value);
            return;
        }

        if (Begin(member))
        {
            DocumentJsonWriter.Write(writer, value);
            End();
        }
    }

    public void WriteStringEnum(int member, string value) => WriteString(member, value);

    public void WriteIntEnum(int member, int value) => WriteInteger(member, value);

    // A member a projection excludes is skipped like any other; only one that would be written is
    // a stream this codec cannot encode.
    public readonly void WriteStream(int member, Stream value)
    {
        if (!IsExcluded(member))
        {
            throw new NotSupportedException("JSON codec does not support streaming blob schemas.");
        }
    }

    public readonly void WriteEventStream<TEvent>(
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

    private readonly bool IsExcluded(int member) =>
        container?.Kind == ShapeKind.Structure && !container.IsIncluded(member);

    public void WriteStruct<T>(int member, T value, IStructSchema<T> schema)
    {
        var entry = Entry(member);
        if (container?.Kind == ShapeKind.Union && container.Discriminator is { } discriminator)
        {
            // A discriminated union's structure case is the structure itself, tagged with the
            // case name: {"type": "case", ...members}.
            writer.WriteStartObject();
            writer.WriteString(discriminator, entry.WireName);
            var inline = new JsonShapeSerializer(writer, entry.Shape!, true, discriminator);
            schema.SerializeMembers(value, ref inline);
            writer.WriteEndObject();
            return;
        }

        if (!Begin(member))
        {
            return;
        }

        writer.WriteStartObject();
        // Only the top-level structure follows the codec's default-materialization option;
        // nested structures always write their defaults.
        var nested = new JsonShapeSerializer(
            writer,
            entry.Shape!,
            member == MemberIndex.Root ? materializeDefaults : true
        );
        schema.SerializeMembers(value, ref nested);
        writer.WriteEndObject();
        End();
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
        var nested = new JsonShapeSerializer(writer, entry.Shape!, true);
        schema.SerializeElements(value, ref nested);
        writer.WriteEndArray();
        End();
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
        var nested = new JsonShapeSerializer(writer, entry.Shape!, true);
        schema.SerializeEntries(value, ref nested);
        writer.WriteEndObject();
        End();
    }

    public void WriteUnion<T>(int member, T value, IUnionSchema<T> schema)
    {
        var entry = Entry(member);
        if (!Begin(member))
        {
            return;
        }

        // The union writes its own wrapping object, or none for an open union's unknown case, so
        // it gets a serializer positioned on the union rather than on its case.
        var nested = new JsonShapeSerializer(writer, entry.Shape!, true);
        schema.SerializeCase(value, ref nested);
        End();
    }
}
