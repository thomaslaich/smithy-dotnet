using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using NSmithy.Codecs;
using NSmithy.Core;
using NSmithy.Core.Annotations;

namespace NSmithy.Codecs.Json;

public sealed class SmithyJsonPayloadCodec : ISmithyPayloadCodec
{
    private static readonly JsonSerializerOptions DefaultOptions = new(JsonSerializerDefaults.Web);

    public static SmithyJsonPayloadCodec Default { get; } = new();

    public string MediaType => "application/json";

    public byte[] Serialize<T>(T value)
    {
        return Encoding.UTF8.GetBytes(SerializeJson(value, DefaultOptions));
    }

    public T Deserialize<T>(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return DeserializeJson<T>(Encoding.UTF8.GetString(content), DefaultOptions);
    }

    private static string SerializeJson<T>(T value, JsonSerializerOptions options)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteValue(writer, value, options);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static T DeserializeJson<T>(string json, JsonSerializerOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        return (T)ReadValue(document.RootElement, typeof(T), options)!;
    }

    private static void WriteValue(
        Utf8JsonWriter writer,
        object? value,
        JsonSerializerOptions options
    )
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        var runtimeType = value.GetType();
        if (value is Document document)
        {
            WriteDocument(writer, document);
            return;
        }

        if (value is byte[] bytes)
        {
            writer.WriteBase64StringValue(bytes);
            return;
        }

        if (value is DateTimeOffset timestamp)
        {
            writer.WriteStringValue(timestamp.ToUniversalTime());
            return;
        }

        var shape = GetShape(runtimeType);
        if (shape is not null)
        {
            WriteShape(writer, value, runtimeType, shape, options);
            return;
        }

        JsonSerializer.Serialize(writer, value, runtimeType, options);
    }

    private static void WriteShape(
        Utf8JsonWriter writer,
        object value,
        Type runtimeType,
        SmithyShapeAttribute shape,
        JsonSerializerOptions options
    )
    {
        switch (shape.Kind)
        {
            case ShapeKind.Structure:
                WriteStructure(writer, value, runtimeType, options);
                break;
            case ShapeKind.List:
            case ShapeKind.Set:
                WriteEnumerable(writer, GetValues(value), options);
                break;
            case ShapeKind.Map:
                WriteMap(writer, GetValues(value), options);
                break;
            case ShapeKind.Enum:
                WriteStringEnum(writer, value);
                break;
            case ShapeKind.IntEnum:
                JsonSerializer.Serialize(writer, value, runtimeType, options);
                break;
            case ShapeKind.Union:
                WriteUnion(writer, value, runtimeType, options);
                break;
            default:
                throw new NotSupportedException(
                    $"Smithy JSON serialization for shape kind '{shape.Kind}' is not supported."
                );
        }
    }

    private static void WriteStructure(
        Utf8JsonWriter writer,
        object value,
        Type type,
        JsonSerializerOptions options
    )
    {
        writer.WriteStartObject();
        foreach (var property in GetSmithyProperties(type))
        {
            var member = property.GetCustomAttribute<SmithyMemberAttribute>()!;
            var propertyValue = property.GetValue(value);
            if (propertyValue is null)
            {
                continue;
            }

            writer.WritePropertyName(member.JsonName ?? member.Name);
            WriteValue(writer, propertyValue, options);
        }

        writer.WriteEndObject();
    }

    private static void WriteEnumerable(
        Utf8JsonWriter writer,
        object? values,
        JsonSerializerOptions options
    )
    {
        writer.WriteStartArray();
        if (values is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                WriteValue(writer, item, options);
            }
        }

        writer.WriteEndArray();
    }

    private static void WriteMap(
        Utf8JsonWriter writer,
        object? values,
        JsonSerializerOptions options
    )
    {
        writer.WriteStartObject();
        if (values is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                var itemType = item.GetType();
                var key = itemType.GetProperty("Key")?.GetValue(item) as string;
                if (key is null)
                {
                    continue;
                }

                var mapValue = itemType.GetProperty("Value")?.GetValue(item);
                writer.WritePropertyName(key);
                WriteValue(writer, mapValue, options);
            }
        }

        writer.WriteEndObject();
    }

    private static void WriteStringEnum(Utf8JsonWriter writer, object value)
    {
        var enumValue = value.GetType().GetProperty("Value")?.GetValue(value) as string;
        writer.WriteStringValue(
            enumValue
                ?? throw new NotSupportedException(
                    $"Smithy string enum '{value.GetType()}' is missing a string Value property."
                )
        );
    }

    private static void WriteUnion(
        Utf8JsonWriter writer,
        object value,
        Type runtimeType,
        JsonSerializerOptions options
    )
    {
        writer.WriteStartObject();
        if (string.Equals(runtimeType.Name, "Unknown", StringComparison.Ordinal))
        {
            var tag = runtimeType.GetProperty("Tag")?.GetValue(value) as string;
            var document = runtimeType.GetProperty("Value")?.GetValue(value);
            writer.WritePropertyName(
                tag ?? throw new InvalidOperationException("Unknown union tag is null.")
            );
            WriteValue(writer, document, options);
        }
        else
        {
            var member =
                runtimeType.GetCustomAttribute<SmithyMemberAttribute>()
                ?? throw new NotSupportedException(
                    $"Union variant '{runtimeType}' is missing Smithy member metadata."
                );
            var variantValue = runtimeType.GetProperty("Value")?.GetValue(value);
            writer.WritePropertyName(member.JsonName ?? member.Name);
            WriteValue(writer, variantValue, options);
        }

        writer.WriteEndObject();
    }

    private static object? ReadValue(
        JsonElement element,
        Type targetType,
        JsonSerializerOptions options
    )
    {
        var nullableType = Nullable.GetUnderlyingType(targetType);
        targetType = nullableType ?? targetType;
        if (targetType == typeof(Document))
        {
            return Document.FromJsonElement(element);
        }

        if (element.ValueKind == JsonValueKind.Null)
        {
            return nullableType is not null || !targetType.IsValueType
                ? null
                : CreateMissingValue(targetType);
        }

        if (targetType == typeof(byte[]))
        {
            return element.GetBytesFromBase64();
        }

        if (targetType == typeof(DateTimeOffset))
        {
            return element.GetDateTimeOffset();
        }

        var shape = GetShape(targetType);
        if (shape is not null)
        {
            return ReadShape(element, targetType, shape, options);
        }

        return element.Deserialize(targetType, options);
    }

    private static object? ReadShape(
        JsonElement element,
        Type targetType,
        SmithyShapeAttribute shape,
        JsonSerializerOptions options
    )
    {
        return shape.Kind switch
        {
            ShapeKind.Structure => ReadStructure(element, targetType, options),
            ShapeKind.List or ShapeKind.Set => ReadList(element, targetType, options),
            ShapeKind.Map => ReadMap(element, targetType, options),
            ShapeKind.Enum => Activator.CreateInstance(targetType, element.GetString()),
            ShapeKind.IntEnum => element.Deserialize(targetType, options),
            ShapeKind.Union => ReadUnion(element, targetType, options),
            _ => throw new NotSupportedException(
                $"Smithy JSON deserialization for shape kind '{shape.Kind}' is not supported."
            ),
        };
    }

    private static object ReadStructure(
        JsonElement element,
        Type targetType,
        JsonSerializerOptions options
    )
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"Expected JSON object for Smithy structure '{targetType}'.");
        }

        var properties = GetSmithyProperties(targetType);
        var constructor = GetPrimaryConstructor(targetType);
        var arguments = new object?[constructor.GetParameters().Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            var parameter = constructor.GetParameters()[i];
            var property = FindConstructorProperty(properties, parameter);
            var member = property.GetCustomAttribute<SmithyMemberAttribute>()!;
            var jsonName = member.JsonName ?? member.Name;
            if (element.TryGetProperty(jsonName, out var propertyElement))
            {
                arguments[i] = ReadValue(propertyElement, parameter.ParameterType, options);
                continue;
            }

            if (IsOptionalParameter(parameter))
            {
                arguments[i] = null;
                continue;
            }

            throw new JsonException(
                $"Required Smithy member '{member.Name}' was missing while deserializing '{targetType}'."
            );
        }

        return constructor.Invoke(arguments);
    }

    private static object ReadList(
        JsonElement element,
        Type targetType,
        JsonSerializerOptions options
    )
    {
        var valuesProperty = GetValuesProperty(targetType);
        var itemType = GetEnumerableItemType(valuesProperty.PropertyType);
        var listType = typeof(List<>).MakeGenericType(itemType);
        var list = (IList)Activator.CreateInstance(listType)!;
        foreach (var item in element.EnumerateArray())
        {
            list.Add(ReadValue(item, itemType, options));
        }

        return GetPrimaryConstructor(targetType).Invoke([list]);
    }

    private static object ReadMap(
        JsonElement element,
        Type targetType,
        JsonSerializerOptions options
    )
    {
        var valuesProperty = GetValuesProperty(targetType);
        var valueType = GetMapValueType(valuesProperty.PropertyType);
        var dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(string), valueType);
        var dictionary = (IDictionary)Activator.CreateInstance(dictionaryType)!;
        foreach (var property in element.EnumerateObject())
        {
            dictionary.Add(property.Name, ReadValue(property.Value, valueType, options));
        }

        return GetPrimaryConstructor(targetType).Invoke([dictionary]);
    }

    private static object ReadUnion(
        JsonElement element,
        Type targetType,
        JsonSerializerOptions options
    )
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"Expected JSON object for Smithy union '{targetType}'.");
        }

        var properties = element.EnumerateObject().ToArray();
        if (properties.Length != 1)
        {
            throw new JsonException(
                $"Expected exactly one member for Smithy union '{targetType}'."
            );
        }

        var jsonProperty = properties[0];
        foreach (var variantType in targetType.GetNestedTypes())
        {
            var member = variantType.GetCustomAttribute<SmithyMemberAttribute>();
            if (member is null || (member.JsonName ?? member.Name) != jsonProperty.Name)
            {
                continue;
            }

            var value = ReadValue(jsonProperty.Value, GetUnionValueType(variantType), options);
            try
            {
                return Activator.CreateInstance(variantType, value)
                    ?? throw new JsonException(
                        $"Activator returned null for union variant '{variantType.Name}' of '{targetType.Name}'."
                    );
            }
            catch (Exception ex) when (ex is not JsonException)
            {
                throw new JsonException(
                    $"Failed to construct union variant '{variantType.Name}' of '{targetType.Name}'.",
                    ex
                );
            }
        }

        var unknown =
            targetType.GetNestedType("Unknown")
            ?? throw new JsonException(
                $"Smithy union '{targetType}' does not support unknown variants."
            );
        try
        {
            return Activator.CreateInstance(
                    unknown,
                    jsonProperty.Name,
                    Document.FromJsonElement(jsonProperty.Value)
                )
                ?? throw new JsonException(
                    $"Activator returned null for unknown variant of '{targetType.Name}'."
                );
        }
        catch (Exception ex) when (ex is not JsonException)
        {
            throw new JsonException(
                $"Failed to construct unknown union variant of '{targetType.Name}'.",
                ex
            );
        }
    }

    private static void WriteDocument(Utf8JsonWriter writer, Document value)
    {
        switch (value.Kind)
        {
            case DocumentKind.Null:
                writer.WriteNullValue();
                break;
            case DocumentKind.Boolean:
                writer.WriteBooleanValue(value.AsBoolean());
                break;
            case DocumentKind.String:
                writer.WriteStringValue(value.AsString());
                break;
            case DocumentKind.Number:
                writer.WriteNumberValue(value.AsNumber());
                break;
            case DocumentKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.AsArray())
                {
                    WriteDocument(writer, item);
                }

                writer.WriteEndArray();
                break;
            case DocumentKind.Object:
                writer.WriteStartObject();
                foreach (var item in value.AsObject())
                {
                    writer.WritePropertyName(item.Key);
                    WriteDocument(writer, item.Value);
                }

                writer.WriteEndObject();
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported Smithy document kind '{value.Kind}'."
                );
        }
    }

    private static IReadOnlyList<PropertyInfo> GetSmithyProperties(Type type)
    {
        return
        [
            .. type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.GetCustomAttribute<SmithyMemberAttribute>() is not null)
                .OrderBy(property => property.Name, StringComparer.Ordinal),
        ];
    }

    private static PropertyInfo FindConstructorProperty(
        IReadOnlyList<PropertyInfo> properties,
        ParameterInfo parameter
    )
    {
        return properties.FirstOrDefault(property =>
                string.Equals(property.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)
            )
            ?? throw new InvalidOperationException(
                $"Constructor parameter '{parameter.Name}' has no Smithy member property."
            );
    }

    private static bool IsOptionalParameter(ParameterInfo parameter)
    {
        var type = parameter.ParameterType;
        return parameter.HasDefaultValue
            || Nullable.GetUnderlyingType(type) is not null
            || !type.IsValueType;
    }

    private static ConstructorInfo GetPrimaryConstructor(Type type)
    {
        return type.GetConstructors(BindingFlags.Instance | BindingFlags.Public)
                .OrderByDescending(constructor => constructor.GetParameters().Length)
                .FirstOrDefault()
            ?? throw new InvalidOperationException($"Type '{type}' has no public constructor.");
    }

    private static object? GetValues(object value)
    {
        return GetValuesProperty(value.GetType()).GetValue(value);
    }

    private static PropertyInfo GetValuesProperty(Type type)
    {
        return type.GetProperty("Values", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new NotSupportedException(
                $"Smithy collection shape '{type}' has no Values property."
            );
    }

    private static Type GetEnumerableItemType(Type type)
    {
        return type.IsGenericType
            ? type.GetGenericArguments()[0]
            : type.GetInterfaces()
                .First(interfaceType =>
                    interfaceType.IsGenericType
                    && interfaceType.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                )
                .GetGenericArguments()[0];
    }

    private static Type GetMapValueType(Type type)
    {
        return type.GetGenericArguments().Length == 2
            ? type.GetGenericArguments()[1]
            : type.GetInterfaces()
                .First(interfaceType =>
                    interfaceType.IsGenericType
                    && interfaceType.GetGenericArguments().Length == 2
                    && interfaceType.GetGenericArguments()[0] == typeof(string)
                )
                .GetGenericArguments()[1];
    }

    private static Type GetUnionValueType(Type variantType)
    {
        return variantType
                .GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)
                ?.PropertyType
            ?? throw new NotSupportedException(
                $"Union variant '{variantType}' has no Value property."
            );
    }

    private static SmithyShapeAttribute? GetShape(Type type)
    {
        return type.GetCustomAttribute<SmithyShapeAttribute>();
    }

    private static object? CreateMissingValue(Type type)
    {
        throw new JsonException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"JSON null cannot be assigned to '{type}'."
            )
        );
    }
}
