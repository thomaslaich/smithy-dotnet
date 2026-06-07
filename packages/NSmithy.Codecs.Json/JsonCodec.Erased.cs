using System.Globalization;
using System.Numerics;
using System.Text.Json;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Json;

public static partial class JsonCodec
{
    private static void WriteValue(Utf8JsonWriter writer, Schema schema, object? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        schema = UnwrapNullable(schema);

        switch (schema.Kind)
        {
            case ShapeKind.Boolean:
                writer.WriteBooleanValue((bool)value);
                break;
            case ShapeKind.Byte:
                writer.WriteNumberValue((sbyte)value);
                break;
            case ShapeKind.Short:
                writer.WriteNumberValue((short)value);
                break;
            case ShapeKind.Integer:
                writer.WriteNumberValue((int)value);
                break;
            case ShapeKind.Long:
                writer.WriteNumberValue((long)value);
                break;
            case ShapeKind.Float:
                WriteFloat(writer, (float)value);
                break;
            case ShapeKind.Double:
                WriteDouble(writer, (double)value);
                break;
            case ShapeKind.BigInteger:
                writer.WriteRawValue(
                    ((BigInteger)value).ToString(CultureInfo.InvariantCulture),
                    skipInputValidation: true
                );
                break;
            case ShapeKind.BigDecimal:
                writer.WriteNumberValue((decimal)value);
                break;
            case ShapeKind.String:
                writer.WriteStringValue((string)value);
                break;
            case ShapeKind.Enum:
                writer.WriteStringValue(((IStringEnumValue)value).Value);
                break;
            case ShapeKind.IntEnum:
                writer.WriteNumberValue(((IIntEnumSchema)schema).GetIntegerValueObject(value));
                break;
            case ShapeKind.Blob:
                writer.WriteBase64StringValue((byte[])value);
                break;
            case ShapeKind.Timestamp:
                TimestampFormat.Write(
                    writer,
                    (DateTimeOffset)value,
                    TimestampFormat.Resolve(null, schema)
                );
                break;
            case ShapeKind.Document:
                DocumentJsonWriter.Write(writer, (Document)value);
                break;
            case ShapeKind.Structure:
                WriteStructure(writer, (IStructSchema)schema, value);
                break;
            case ShapeKind.Union:
                WriteUnion(writer, (IUnionSchema)schema, value);
                break;
            case ShapeKind.List:
            case ShapeKind.Set:
                WriteList(writer, (IListSchema)schema, value);
                break;
            case ShapeKind.Map:
                WriteMap(writer, (IMapSchema)schema, value);
                break;
            default:
                throw new NotSupportedException(
                    $"JSON codec does not support schema kind '{schema.Kind}'."
                );
        }
    }

    private static void WriteValue<T>(Utf8JsonWriter writer, Schema<T> schema, T value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        if (schema.Resolved is IStructSchema<T> structSchema)
        {
            WriteStructure(writer, structSchema, value);
            return;
        }

        WriteValue(writer, (Schema)schema, value);
    }

    private static void WriteStructure<T>(Utf8JsonWriter writer, IStructSchema<T> schema, T value)
    {
        writer.WriteStartObject();
        schema.VisitMembers(new JsonWriteMemberVisitor<T>(writer, value));
        writer.WriteEndObject();
    }

    private static void WriteProjection<T>(
        Utf8JsonWriter writer,
        StructProjection<T> projection,
        T value
    )
    {
        writer.WriteStartObject();
        projection.VisitMembers(new JsonWriteMemberVisitor<T>(writer, value));
        writer.WriteEndObject();
    }

    private sealed class JsonWriteMemberVisitor<TContainer>(
        Utf8JsonWriter writer,
        TContainer container
    ) : IMemberVisitor<TContainer>
    {
        public void Visit<TValue>(IMemberSchema<TContainer, TValue> member)
        {
            var memberValue = member.GetValue(container);
            if (memberValue is null && !member.IsRequired)
            {
                if (
                    !TryCreateDefaultValue(member.TargetSchema, member.Traits, out var defaultValue)
                )
                {
                    return;
                }

                memberValue = (TValue)defaultValue!;
            }

            writer.WritePropertyName(WireName(member.Traits, member.Name));
            WriteValue(writer, member.TargetSchema, memberValue);
        }
    }

    private static void WriteStructure(Utf8JsonWriter writer, IStructSchema schema, object value)
    {
        writer.WriteStartObject();
        foreach (var member in schema.Members)
        {
            var memberValue = member.GetObject(value);
            if (memberValue is null && !member.IsRequired)
            {
                if (!TryCreateDefaultValue(member.Target, member.Traits, out memberValue))
                {
                    continue;
                }
            }

            writer.WritePropertyName(WireName(member.Traits, member.Name));
            WriteValue(writer, member.Target, memberValue);
        }

        writer.WriteEndObject();
    }

    private static void WriteUnion(Utf8JsonWriter writer, IUnionSchema schema, object value)
    {
        if (TryGetDiscriminatorName(schema, out var discriminatorName))
        {
            WriteDiscriminatedUnion(writer, schema, discriminatorName, value);
            return;
        }

        var @case = schema.GetCaseObject(value);
        if (IsJsonUnknownCase(@case))
        {
            WriteValue(writer, @case.Target, @case.GetObject(value));
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName(WireName(@case.Traits, @case.Name));
        WriteValue(writer, @case.Target, @case.GetObject(value));
        writer.WriteEndObject();
    }

    private static void WriteDiscriminatedUnion(
        Utf8JsonWriter writer,
        IUnionSchema schema,
        string discriminatorName,
        object value
    )
    {
        var @case = schema.GetCaseObject(value);
        var caseValue = @case.GetObject(value);
        if (IsJsonUnknownCase(@case))
        {
            WriteValue(writer, @case.Target, caseValue);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString(discriminatorName, WireName(@case.Traits, @case.Name));
        using var buffer = new MemoryStream();
        using (var bufferedWriter = new Utf8JsonWriter(buffer))
        {
            WriteValue(bufferedWriter, @case.Target, caseValue);
        }

        using var document = JsonDocument.Parse(buffer.ToArray());
        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.NameEquals(discriminatorName))
                {
                    continue;
                }

                property.WriteTo(writer);
            }
        }
        else
        {
            writer.WritePropertyName("value");
            document.RootElement.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    private static void WriteList(Utf8JsonWriter writer, IListSchema schema, object value)
    {
        writer.WriteStartArray();
        foreach (var element in schema.GetElementsObject(value))
        {
            WriteValue(writer, schema.Element, element);
        }

        writer.WriteEndArray();
    }

    private static void WriteMap(Utf8JsonWriter writer, IMapSchema schema, object value)
    {
        writer.WriteStartObject();
        foreach (var entry in schema.GetEntriesObject(value))
        {
            writer.WritePropertyName(entry.Key);
            WriteValue(writer, schema.Value, entry.Value);
        }

        writer.WriteEndObject();
    }

    private static object? ReadValue(Schema schema, JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        schema = UnwrapNullable(schema);

        return schema.Kind switch
        {
            ShapeKind.Boolean => value.GetBoolean(),
            ShapeKind.Byte => value.GetSByte(),
            ShapeKind.Short => value.GetInt16(),
            ShapeKind.Integer => value.GetInt32(),
            ShapeKind.Long => value.GetInt64(),
            ShapeKind.Float => ReadFloat(value),
            ShapeKind.Double => ReadDouble(value),
            ShapeKind.BigInteger => BigInteger.Parse(
                value.GetRawText(),
                CultureInfo.InvariantCulture
            ),
            ShapeKind.BigDecimal => value.GetDecimal(),
            ShapeKind.String => value.GetString(),
            ShapeKind.Enum => ((IStringEnumSchema)schema).CreateObject(value.GetString()!),
            ShapeKind.IntEnum => ((IIntEnumSchema)schema).CreateObject(value.GetInt32()),
            ShapeKind.Blob => value.GetBytesFromBase64(),
            ShapeKind.Timestamp => TimestampFormat.Read(
                value,
                TimestampFormat.Resolve(null, schema)
            ),
            ShapeKind.Document => Document.FromJsonElement(value),
            ShapeKind.Structure => ReadStructure((IStructSchema)schema, value),
            ShapeKind.Union => ReadUnion((IUnionSchema)schema, value),
            ShapeKind.List or ShapeKind.Set => ReadList((IListSchema)schema, value),
            ShapeKind.Map => ReadMap((IMapSchema)schema, value),
            _ => throw new NotSupportedException(
                $"JSON codec does not support schema kind '{schema.Kind}'."
            ),
        };
    }

    private static Schema UnwrapNullable(Schema schema)
    {
        var resolved = schema.Resolved;
        return resolved is INullableSchema nullable ? nullable.Target.Resolved : resolved;
    }

    private static readonly ShapeId ClientOptionalTrait = new("smithy.api", "clientOptional");
    private static readonly ShapeId DefaultTrait = new("smithy.api", "default");
    private static readonly ShapeId JsonNameTrait = new("smithy.api", "jsonName");
    private static readonly ShapeId AlloyDiscriminatedTrait = new("alloy", "discriminated");
    private static readonly ShapeId AlloyJsonUnknownTrait = new("alloy", "jsonUnknown");

    // The JSON property name for a member or union case: @jsonName if present, else the name.
    private static string WireName(IReadOnlyDictionary<ShapeId, Trait> traits, string fallback) =>
        traits.TryGetValue(JsonNameTrait, out var trait) ? trait.Value.AsString() : fallback;

    private static bool IsOpenUnion(IUnionSchema schema) =>
        ((Schema)schema).Traits.ContainsKey(AlloyDiscriminatedTrait)
        || GetJsonUnknownCase(schema) is not null;

    private static bool TryCreateDefaultValue(
        Schema schema,
        IReadOnlyDictionary<ShapeId, Trait> traits,
        out object? value
    )
    {
        if (
            traits.ContainsKey(ClientOptionalTrait)
            || !traits.TryGetValue(DefaultTrait, out var trait)
            || trait.Value.Kind == DocumentKind.Null
        )
        {
            value = null;
            return false;
        }

        value = CreateDefaultValue(UnwrapNullable(schema), trait.Value);
        return value is not null;
    }

    private static object? CreateDefaultValue(Schema schema, Document value)
    {
        return schema.Kind switch
        {
            ShapeKind.Boolean => value.AsBoolean(),
            ShapeKind.Byte => (sbyte)value.AsNumber(),
            ShapeKind.Short => (short)value.AsNumber(),
            ShapeKind.Integer => (int)value.AsNumber(),
            ShapeKind.Long => (long)value.AsNumber(),
            ShapeKind.Float => (float)value.AsNumber(),
            ShapeKind.Double => (double)value.AsNumber(),
            ShapeKind.BigInteger => new BigInteger(value.AsNumber()),
            ShapeKind.BigDecimal => value.AsNumber(),
            ShapeKind.String => value.AsString(),
            ShapeKind.Enum => ((IStringEnumSchema)schema).CreateObject(value.AsString()),
            ShapeKind.IntEnum => ((IIntEnumSchema)schema).CreateObject((int)value.AsNumber()),
            ShapeKind.Blob => Convert.FromBase64String(value.AsString()),
            ShapeKind.Timestamp => DateTimeOffset.FromUnixTimeSeconds((long)value.AsNumber()),
            ShapeKind.Document => value,
            ShapeKind.List or ShapeKind.Set when schema.Resolved is IListSchema list =>
                CreateDefaultList(list, value),
            ShapeKind.Map when schema.Resolved is IMapSchema map => CreateDefaultMap(map, value),
            _ => null,
        };
    }

    private static object CreateDefaultList(IListSchema schema, Document value)
    {
        var builder = schema.CreateBuilder();
        foreach (var item in value.AsArray())
        {
            schema.AddObject(builder, CreateDefaultValue(UnwrapNullable(schema.Element), item));
        }

        return schema.BuildObject(builder);
    }

    private static object CreateDefaultMap(IMapSchema schema, Document value)
    {
        var builder = schema.CreateBuilder();
        foreach (var entry in value.AsObject())
        {
            schema.AddObject(
                builder,
                entry.Key,
                CreateDefaultValue(UnwrapNullable(schema.Value), entry.Value)
            );
        }

        return schema.BuildObject(builder);
    }

    private static bool TryGetDiscriminatorName(IUnionSchema schema, out string discriminatorName)
    {
        if (((Schema)schema).Traits.TryGetValue(AlloyDiscriminatedTrait, out var trait))
        {
            discriminatorName = trait.Value.AsString();
            return true;
        }

        discriminatorName = string.Empty;
        return false;
    }

    private static IUnionCaseSchema? GetJsonUnknownCase(IUnionSchema schema) =>
        schema.Cases.FirstOrDefault(IsJsonUnknownCase);

    private static bool IsJsonUnknownCase(IUnionCaseSchema @case) =>
        @case.Traits.ContainsKey(AlloyJsonUnknownTrait);

    private static object ReadStructure(IStructSchema schema, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"Expected JSON object but found {value.ValueKind}."
            );
        }

        var builder = schema.CreateBuilder();
        foreach (var member in schema.Members)
        {
            if (!value.TryGetProperty(WireName(member.Traits, member.Name), out var memberValue))
            {
                if (member.IsRequired)
                {
                    throw new InvalidOperationException(
                        $"Missing required member '{member.Name}'."
                    );
                }

                if (TryCreateDefaultValue(member.Target, member.Traits, out var defaultValue))
                {
                    member.SetObject(builder, defaultValue);
                }

                continue;
            }

            if (memberValue.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                if (member.IsRequired)
                {
                    throw new InvalidOperationException(
                        $"Required member '{member.Name}' cannot be null."
                    );
                }

                if (TryCreateDefaultValue(member.Target, member.Traits, out var defaultValue))
                {
                    member.SetObject(builder, defaultValue);
                }

                continue;
            }

            member.SetObject(builder, ReadValue(member.Target, memberValue));
        }

        return schema.BuildObject(builder);
    }

    private static void ReadProjectionInto<T>(
        StructProjection<T> projection,
        JsonElement value,
        object builder
    )
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"Expected JSON object but found {value.ValueKind}."
            );
        }

        foreach (var member in projection.TypedMembers)
        {
            if (!value.TryGetProperty(WireName(member.Traits, member.Name), out var memberValue))
            {
                if (member.IsRequired)
                {
                    throw new InvalidOperationException(
                        $"Missing required member '{member.Name}'."
                    );
                }

                continue;
            }

            if (memberValue.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                if (member.IsRequired)
                {
                    throw new InvalidOperationException(
                        $"Required member '{member.Name}' cannot be null."
                    );
                }

                continue;
            }

            member.SetObject(builder, ReadValue(member.Target, memberValue));
        }
    }

    private static object ReadUnion(IUnionSchema schema, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"Expected JSON object but found {value.ValueKind}."
            );
        }

        if (TryGetDiscriminatorName(schema, out var discriminatorName))
        {
            return ReadDiscriminatedUnion(schema, discriminatorName, value);
        }

        var properties = value
            .EnumerateObject()
            .Where(property => !property.NameEquals("__type"))
            .ToArray();
        if (properties.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected union value to contain exactly one member but found {properties.Length}."
            );
        }

        var property = properties[0];
        var @case = schema.Cases.FirstOrDefault(c => WireName(c.Traits, c.Name) == property.Name);
        if (@case is null)
        {
            var unknownCase =
                GetJsonUnknownCase(schema)
                ?? throw new InvalidOperationException($"Unknown union member '{property.Name}'.");
            return unknownCase.CreateObject(Document.FromJsonElement(value));
        }

        return @case.CreateObject(ReadValue(@case.Target, property.Value));
    }

    private static object ReadDiscriminatedUnion(
        IUnionSchema schema,
        string discriminatorName,
        JsonElement value
    )
    {
        if (
            value.TryGetProperty(discriminatorName, out var discriminator)
            && discriminator.ValueKind == JsonValueKind.String
        )
        {
            var tag = discriminator.GetString()!;
            var @case = schema.Cases.FirstOrDefault(c => WireName(c.Traits, c.Name) == tag);
            if (@case is not null && !IsJsonUnknownCase(@case))
            {
                using var buffer = new MemoryStream();
                using (var writer = new Utf8JsonWriter(buffer))
                {
                    writer.WriteStartObject();
                    foreach (var property in value.EnumerateObject())
                    {
                        if (!property.NameEquals(discriminatorName))
                        {
                            property.WriteTo(writer);
                        }
                    }
                    writer.WriteEndObject();
                }

                using var document = JsonDocument.Parse(buffer.ToArray());
                return @case.CreateObject(ReadValue(@case.Target, document.RootElement));
            }
        }

        var unknownCase =
            GetJsonUnknownCase(schema)
            ?? throw new InvalidOperationException(
                $"Discriminated union '{((Schema)schema).Id}' is missing an unknown JSON case."
            );
        return unknownCase.CreateObject(Document.FromJsonElement(value));
    }

    private static object ReadList(IListSchema schema, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                $"Expected JSON array but found {value.ValueKind}."
            );
        }

        var builder = schema.CreateBuilder();
        foreach (var element in value.EnumerateArray())
        {
            schema.AddObject(builder, ReadValue(schema.Element, element));
        }

        return schema.BuildObject(builder);
    }

    private static object ReadMap(IMapSchema schema, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"Expected JSON object but found {value.ValueKind}."
            );
        }

        var builder = schema.CreateBuilder();
        foreach (var property in value.EnumerateObject())
        {
            schema.AddObject(builder, property.Name, ReadValue(schema.Value, property.Value));
        }

        return schema.BuildObject(builder);
    }

    private static void WriteFloat(Utf8JsonWriter writer, float value)
    {
        if (float.IsNaN(value))
        {
            writer.WriteStringValue("NaN");
        }
        else if (float.IsPositiveInfinity(value))
        {
            writer.WriteStringValue("Infinity");
        }
        else if (float.IsNegativeInfinity(value))
        {
            writer.WriteStringValue("-Infinity");
        }
        else
        {
            writer.WriteNumberValue(value);
        }
    }

    private static void WriteDouble(Utf8JsonWriter writer, double value)
    {
        if (double.IsNaN(value))
        {
            writer.WriteStringValue("NaN");
        }
        else if (double.IsPositiveInfinity(value))
        {
            writer.WriteStringValue("Infinity");
        }
        else if (double.IsNegativeInfinity(value))
        {
            writer.WriteStringValue("-Infinity");
        }
        else
        {
            writer.WriteNumberValue(value);
        }
    }

    private static float ReadFloat(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
            ? value.GetString() switch
            {
                "NaN" => float.NaN,
                "Infinity" => float.PositiveInfinity,
                "-Infinity" => float.NegativeInfinity,
                var s => float.Parse(s!, CultureInfo.InvariantCulture),
            }
            : value.GetSingle();

    private static double ReadDouble(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
            ? value.GetString() switch
            {
                "NaN" => double.NaN,
                "Infinity" => double.PositiveInfinity,
                "-Infinity" => double.NegativeInfinity,
                var s => double.Parse(s!, CultureInfo.InvariantCulture),
            }
            : value.GetDouble();
}
