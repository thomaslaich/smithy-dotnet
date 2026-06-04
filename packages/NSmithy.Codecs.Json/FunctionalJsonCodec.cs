using System.Globalization;
using System.Numerics;
using System.Text.Json;
using NSmithy.Core;
using NSmithy.Core.Functional;

namespace NSmithy.Codecs.Json;

public interface IFunctionalJsonCodec<T> : IFunctionalCodec<T, string> { }

public interface IFunctionalJsonObjectCodec : IFunctionalObjectCodec<string> { }

public static class FunctionalJsonCodec
{
    public static IFunctionalJsonObjectCodec FromSchema(FunctionalSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new CompiledFunctionalJsonObjectCodec(schema);
    }

    public static IFunctionalJsonCodec<T> FromSchema<T>(FunctionalSchema<T> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new CompiledFunctionalJsonCodec<T>(schema);
    }

    private sealed class CompiledFunctionalJsonCodec<T>(FunctionalSchema<T> schema)
        : IFunctionalJsonCodec<T>
    {
        private readonly IFunctionalJsonObjectCodec objectCodec = FromSchema(
            (FunctionalSchema)schema
        );

        public string Serialize(T value)
        {
            return objectCodec.Serialize(value);
        }

        public T Deserialize(string payload)
        {
            return (T)objectCodec.Deserialize(payload)!;
        }
    }

    private sealed class CompiledFunctionalJsonObjectCodec(FunctionalSchema schema)
        : IFunctionalJsonObjectCodec
    {
        public string Serialize(object? value)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                WriteValue(writer, schema, value);
            }

            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }

        public object? Deserialize(string payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            using var document = JsonDocument.Parse(payload);
            return ReadValue(schema, document.RootElement);
        }
    }

    private static void WriteValue(Utf8JsonWriter writer, FunctionalSchema schema, object? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

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
            case ShapeKind.Blob:
                writer.WriteBase64StringValue((byte[])value);
                break;
            case ShapeKind.Timestamp:
                writer.WriteStringValue(
                    ((DateTimeOffset)value).ToString("O", CultureInfo.InvariantCulture)
                );
                break;
            case ShapeKind.Document:
                DocumentJsonWriter.Write(writer, (Document)value);
                break;
            case ShapeKind.Structure:
                WriteStructure(writer, (IFunctionalStructSchema)schema, value);
                break;
            case ShapeKind.Union:
                WriteUnion(writer, (IFunctionalUnionSchema)schema, value);
                break;
            case ShapeKind.List:
            case ShapeKind.Set:
                WriteList(writer, (IFunctionalListSchema)schema, value);
                break;
            case ShapeKind.Map:
                WriteMap(writer, (IFunctionalMapSchema)schema, value);
                break;
            default:
                throw new NotSupportedException(
                    $"JSON codec does not support schema kind '{schema.Kind}'."
                );
        }
    }

    private static void WriteStructure(
        Utf8JsonWriter writer,
        IFunctionalStructSchema schema,
        object value
    )
    {
        writer.WriteStartObject();
        foreach (var member in schema.Members)
        {
            var memberValue = member.GetObject(value);
            if (memberValue is null && !member.IsRequired)
            {
                continue;
            }

            writer.WritePropertyName(member.Name);
            WriteValue(writer, member.Target, memberValue);
        }

        writer.WriteEndObject();
    }

    private static void WriteUnion(
        Utf8JsonWriter writer,
        IFunctionalUnionSchema schema,
        object value
    )
    {
        var @case = schema.GetCaseObject(value);
        writer.WriteStartObject();
        writer.WritePropertyName(@case.Name);
        WriteValue(writer, @case.Target, @case.GetObject(value));
        writer.WriteEndObject();
    }

    private static void WriteList(Utf8JsonWriter writer, IFunctionalListSchema schema, object value)
    {
        writer.WriteStartArray();
        foreach (var element in schema.GetElementsObject(value))
        {
            WriteValue(writer, schema.Element, element);
        }

        writer.WriteEndArray();
    }

    private static void WriteMap(Utf8JsonWriter writer, IFunctionalMapSchema schema, object value)
    {
        writer.WriteStartObject();
        foreach (var entry in schema.GetEntriesObject(value))
        {
            writer.WritePropertyName(entry.Key);
            WriteValue(writer, schema.Value, entry.Value);
        }

        writer.WriteEndObject();
    }

    private static object? ReadValue(FunctionalSchema schema, JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

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
            ShapeKind.Blob => value.GetBytesFromBase64(),
            ShapeKind.Timestamp => DateTimeOffset.Parse(
                value.GetString()!,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind
            ),
            ShapeKind.Document => Document.FromJsonElement(value),
            ShapeKind.Structure => ReadStructure((IFunctionalStructSchema)schema, value),
            ShapeKind.Union => ReadUnion((IFunctionalUnionSchema)schema, value),
            ShapeKind.List or ShapeKind.Set => ReadList((IFunctionalListSchema)schema, value),
            ShapeKind.Map => ReadMap((IFunctionalMapSchema)schema, value),
            _ => throw new NotSupportedException(
                $"JSON codec does not support schema kind '{schema.Kind}'."
            ),
        };
    }

    private static object ReadStructure(IFunctionalStructSchema schema, JsonElement value)
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
            if (!value.TryGetProperty(member.Name, out var memberValue))
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

                member.SetObject(builder, null);
                continue;
            }

            member.SetObject(builder, ReadValue(member.Target, memberValue));
        }

        return schema.BuildObject(builder);
    }

    private static object ReadUnion(IFunctionalUnionSchema schema, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"Expected JSON object but found {value.ValueKind}."
            );
        }

        var properties = value.EnumerateObject().ToArray();
        if (properties.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected union value to contain exactly one member but found {properties.Length}."
            );
        }

        var property = properties[0];
        var @case =
            schema.GetCase(property.Name)
            ?? throw new InvalidOperationException($"Unknown union member '{property.Name}'.");
        return @case.CreateObject(ReadValue(@case.Target, property.Value));
    }

    private static object ReadList(IFunctionalListSchema schema, JsonElement value)
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

    private static object ReadMap(IFunctionalMapSchema schema, JsonElement value)
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
