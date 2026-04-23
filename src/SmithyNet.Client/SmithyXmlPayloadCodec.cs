using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using SmithyNet.Core;
using SmithyNet.Core.Annotations;

namespace SmithyNet.Client;

public sealed class SmithyXmlPayloadCodec : ISmithyPayloadCodec
{
    private const string XmlNameTraitId = "smithy.api#xmlName";
    private const string XmlFlattenedTraitId = "smithy.api#xmlFlattened";
    private const string XmlAttributeTraitId = "smithy.api#xmlAttribute";
    private const string TimestampFormatTraitId = "smithy.api#timestampFormat";

    public static SmithyXmlPayloadCodec Default { get; } = new();

    public string MediaType => "application/xml";

    public byte[] Serialize<T>(T value)
    {
        var element = CreateTopLevelElement(value, typeof(T));
        return SerializeElement(element);
    }

    public byte[] SerializeMembers(string rootName, IReadOnlyDictionary<string, object?> members)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootName);
        ArgumentNullException.ThrowIfNull(members);

        var root = new XElement(rootName);
        foreach (var member in members)
        {
            if (member.Value is null)
            {
                continue;
            }

            foreach (var child in CreateMemberNodes(member.Key, member.Value, null, null, isFlattened: false))
            {
                root.Add(child);
            }
        }

        return SerializeElement(root);
    }

    public T Deserialize<T>(byte[] content)
    {
        var root = ParseRoot(content);
        return (T)ReadValue(root, typeof(T), null)!;
    }

    public T DeserializeMember<T>(byte[] content, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var root = ParseRoot(content);
        var member = root.Elements().FirstOrDefault(element => element.Name.LocalName == name);
        return member is null ? default! : (T)ReadValue(member, typeof(T), null)!;
    }

    private static byte[] SerializeElement(XElement element)
    {
        var document = new XDocument(element);
        return Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));
    }

    private static XElement ParseRoot(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0)
        {
            throw new InvalidOperationException("XML payload was empty.");
        }

        using var stream = new MemoryStream(content, writable: false);
        return XDocument.Load(stream, LoadOptions.None).Root
            ?? throw new InvalidOperationException("XML payload is missing a root element.");
    }

    private static XElement CreateTopLevelElement(object? value, Type declaredType)
    {
        if (value is null)
        {
            throw new InvalidOperationException("XML payload cannot be null.");
        }

        var type = value.GetType();
        var shape = GetShape(type) ?? GetShape(declaredType);
        var name = GetTopLevelElementName(shape, type);
        var element = new XElement(name);
        WriteValueIntoElement(element, value, declaredType, memberMetadata: null);
        return element;
    }

    private static string GetTopLevelElementName(SmithyShapeAttribute? shape, Type type)
    {
        if (shape is not null && !string.IsNullOrWhiteSpace(GetTraitValue(type, XmlNameTraitId)))
        {
            return GetTraitValue(type, XmlNameTraitId)!;
        }

        return shape?.Id.Split('#')[1]
            ?? throw new NotSupportedException($"Cannot derive XML root element name for '{type}'.");
    }

    private static void WriteValueIntoElement(
        XElement element,
        object value,
        Type declaredType,
        PropertyInfo? memberMetadata
    )
    {
        var runtimeType = value.GetType();
        if (value is Document)
        {
            throw new NotSupportedException("Smithy document values are not supported by restXml.");
        }

        if (value is byte[] bytes)
        {
            element.Value = Convert.ToBase64String(bytes);
            return;
        }

        if (value is DateTimeOffset timestamp)
        {
            element.Value = FormatTimestamp(timestamp, memberMetadata);
            return;
        }

        var shape = GetShape(runtimeType) ?? GetShape(declaredType);
        if (shape is not null)
        {
            switch (shape.Kind)
            {
                case ShapeKind.Structure:
                    WriteStructure(element, value, runtimeType);
                    return;
                case ShapeKind.List:
                case ShapeKind.Set:
                    WriteList(element, value, runtimeType, memberMetadata);
                    return;
                case ShapeKind.Map:
                    WriteMap(element, value, runtimeType, memberMetadata);
                    return;
                case ShapeKind.Enum:
                    element.Value =
                        runtimeType.GetProperty("Value")?.GetValue(value) as string
                        ?? throw new NotSupportedException(
                            $"Smithy string enum '{runtimeType}' is missing a string Value property."
                        );
                    return;
                case ShapeKind.IntEnum:
                    element.Value = Convert.ToInt32(value, CultureInfo.InvariantCulture)
                        .ToString(CultureInfo.InvariantCulture);
                    return;
                case ShapeKind.Union:
                    WriteUnion(element, value, runtimeType);
                    return;
                default:
                    throw new NotSupportedException(
                        $"Smithy XML serialization for shape kind '{shape.Kind}' is not supported."
                    );
            }
        }

        element.Value = FormatScalar(value);
    }

    private static void WriteStructure(XElement element, object value, Type type)
    {
        foreach (var property in GetSmithyProperties(type))
        {
            var propertyValue = property.GetValue(value);
            if (propertyValue is null)
            {
                continue;
            }

            if (HasTrait(property, XmlAttributeTraitId))
            {
                element.SetAttributeValue(GetMemberElementName(property), FormatScalar(propertyValue));
                continue;
            }

            var isFlattened = HasTrait(property, XmlFlattenedTraitId);
            foreach (
                var child in CreateMemberNodes(
                    GetMemberElementName(property),
                    propertyValue,
                    property.PropertyType,
                    property,
                    isFlattened
                )
            )
            {
                element.Add(child);
            }
        }
    }

    private static List<XElement> CreateMemberNodes(
        string name,
        object value,
        Type? declaredType,
        PropertyInfo? memberMetadata,
        bool isFlattened
    )
    {
        declaredType ??= value.GetType();
        var shape = GetShape(value.GetType()) ?? GetShape(declaredType);
        if (isFlattened && shape is not null)
        {
            switch (shape.Kind)
            {
                case ShapeKind.List:
                case ShapeKind.Set:
                    return FlattenList(name, value);
                case ShapeKind.Map:
                    return FlattenMap(name, value);
            }
        }

        var child = new XElement(name);
        WriteValueIntoElement(child, value, declaredType, memberMetadata);
        return [child];
    }

    private static List<XElement> FlattenList(string name, object value)
    {
        if (GetValues(value) is not IEnumerable enumerable)
        {
            return [];
        }

        var elements = new List<XElement>();
        foreach (var item in enumerable)
        {
            if (item is null)
            {
                continue;
            }

            var child = new XElement(name);
            WriteValueIntoElement(child, item, item.GetType(), null);
            elements.Add(child);
        }

        return elements;
    }

    private static List<XElement> FlattenMap(string name, object value)
    {
        if (GetValues(value) is not IEnumerable enumerable)
        {
            return [];
        }

        var elements = new List<XElement>();
        foreach (var item in enumerable)
        {
            if (item is null)
            {
                continue;
            }

            var entry = new XElement(name);
            WriteMapEntry(entry, item);
            elements.Add(entry);
        }

        return elements;
    }

    private static void WriteList(XElement element, object value, Type type, PropertyInfo? memberMetadata)
    {
        if (GetValues(value) is not IEnumerable enumerable)
        {
            return;
        }

        var memberName = GetCollectionItemName(type, memberMetadata);
        foreach (var item in enumerable)
        {
            if (item is null)
            {
                continue;
            }

            var child = new XElement(memberName);
            WriteValueIntoElement(child, item, item.GetType(), null);
            element.Add(child);
        }
    }

    private static void WriteMap(XElement element, object value, Type type, PropertyInfo? memberMetadata)
    {
        if (GetValues(value) is not IEnumerable enumerable)
        {
            return;
        }

        var entryName = GetCollectionItemName(type, memberMetadata);
        foreach (var item in enumerable)
        {
            if (item is null)
            {
                continue;
            }

            var entry = new XElement(entryName);
            WriteMapEntry(entry, item);
            element.Add(entry);
        }
    }

    private static void WriteMapEntry(XElement entry, object item)
    {
        if (item is DictionaryEntry dictionaryEntry)
        {
            entry.Add(new XElement("key", dictionaryEntry.Key?.ToString() ?? string.Empty));
            if (dictionaryEntry.Value is not null)
            {
                var valueElement = new XElement("value");
                WriteValueIntoElement(
                    valueElement,
                    dictionaryEntry.Value,
                    dictionaryEntry.Value.GetType(),
                    null
                );
                entry.Add(valueElement);
            }

            return;
        }

        var itemType = item.GetType();
        var key = itemType.GetProperty("Key")?.GetValue(item)?.ToString() ?? string.Empty;
        entry.Add(new XElement("key", key));
        var mapValue = itemType.GetProperty("Value")?.GetValue(item);
        if (mapValue is not null)
        {
            var valueElement = new XElement("value");
            WriteValueIntoElement(valueElement, mapValue, mapValue.GetType(), null);
            entry.Add(valueElement);
        }
    }

    private static void WriteUnion(XElement element, object value, Type runtimeType)
    {
        if (string.Equals(runtimeType.Name, "Unknown", StringComparison.Ordinal))
        {
            throw new NotSupportedException("Unknown union variants are not supported by restXml.");
        }

        var member =
            runtimeType.GetCustomAttribute<SmithyMemberAttribute>()
            ?? throw new NotSupportedException(
                $"Union variant '{runtimeType}' is missing Smithy member metadata."
            );
        var child = new XElement(GetMemberElementName(runtimeType, member));
        var unionValue = runtimeType.GetProperty("Value")?.GetValue(value);
        if (unionValue is not null)
        {
            WriteValueIntoElement(child, unionValue, GetUnionValueType(runtimeType), null);
        }

        element.Add(child);
    }

    private static object? ReadValue(XElement element, Type targetType, PropertyInfo? memberMetadata)
    {
        var nullableType = Nullable.GetUnderlyingType(targetType);
        targetType = nullableType ?? targetType;
        if (targetType == typeof(Document))
        {
            throw new NotSupportedException("Smithy document values are not supported by restXml.");
        }

        if (targetType == typeof(byte[]))
        {
            return Convert.FromBase64String(element.Value);
        }

        if (targetType == typeof(DateTimeOffset))
        {
            return ParseTimestamp(element.Value, memberMetadata);
        }

        var shape = GetShape(targetType);
        if (shape is not null)
        {
            return shape.Kind switch
            {
                ShapeKind.Structure => ReadStructure(element, targetType),
                ShapeKind.List or ShapeKind.Set => ReadList(element, targetType, memberMetadata),
                ShapeKind.Map => ReadMap(element, targetType, memberMetadata),
                ShapeKind.Enum => Activator.CreateInstance(targetType, element.Value),
                ShapeKind.IntEnum => Enum.ToObject(
                    targetType,
                    int.Parse(element.Value, CultureInfo.InvariantCulture)
                ),
                ShapeKind.Union => ReadUnion(element, targetType),
                _ => throw new NotSupportedException(
                    $"Smithy XML deserialization for shape kind '{shape.Kind}' is not supported."
                ),
            };
        }

        if (targetType == typeof(string))
        {
            return element.Value;
        }

        if (targetType.IsEnum)
        {
            return Enum.Parse(targetType, element.Value, ignoreCase: false);
        }

        return Convert.ChangeType(element.Value, targetType, CultureInfo.InvariantCulture);
    }

    private static object ReadStructure(XElement element, Type targetType)
    {
        var constructor = GetPrimaryConstructor(targetType);
        var properties = GetSmithyProperties(targetType);
        var arguments = new object?[constructor.GetParameters().Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            var parameter = constructor.GetParameters()[i];
            if (string.Equals(parameter.Name, "message", StringComparison.OrdinalIgnoreCase))
            {
                var messageProperty = properties.FirstOrDefault(property =>
                    string.Equals(property.Name, "Message", StringComparison.Ordinal)
                );
                arguments[i] = ReadStructureMember(element, messageProperty, parameter.ParameterType);
                continue;
            }

            var property = FindConstructorProperty(properties, parameter);
            arguments[i] = ReadStructureMember(element, property, parameter.ParameterType);
        }

        return constructor.Invoke(arguments);
    }

    private static object? ReadStructureMember(
        XElement element,
        PropertyInfo? property,
        Type parameterType
    )
    {
        if (property is null)
        {
            return IsOptionalParameter(parameterType) ? null : CreateMissingValue(parameterType);
        }

        if (HasTrait(property, XmlAttributeTraitId))
        {
            var attribute = element.Attributes().FirstOrDefault(attribute =>
                attribute.Name.LocalName == GetMemberElementName(property)
            );
            return attribute is null
                ? (IsOptionalParameter(parameterType) ? null : CreateMissingValue(parameterType))
                : Convert.ChangeType(attribute.Value, Nullable.GetUnderlyingType(parameterType) ?? parameterType, CultureInfo.InvariantCulture);
        }

        var memberName = GetMemberElementName(property);
        var memberShape = GetShape(Nullable.GetUnderlyingType(parameterType) ?? parameterType);
        if (HasTrait(property, XmlFlattenedTraitId) && memberShape is { Kind: ShapeKind.List or ShapeKind.Set or ShapeKind.Map })
        {
            return ReadValue(
                new XElement(memberName, element.Elements().Where(child => child.Name.LocalName == memberName)),
                parameterType,
                property
            );
        }

        var child = element.Elements().FirstOrDefault(node => node.Name.LocalName == memberName);
        return child is null
            ? (IsOptionalParameter(parameterType) ? null : CreateMissingValue(parameterType))
            : ReadValue(child, parameterType, property);
    }

    private static object ReadList(XElement element, Type targetType, PropertyInfo? memberMetadata)
    {
        var valuesProperty = GetValuesProperty(targetType);
        var itemType = GetEnumerableItemType(valuesProperty.PropertyType);
        var listType = typeof(List<>).MakeGenericType(itemType);
        var list = (IList)Activator.CreateInstance(listType)!;
        var itemName = GetCollectionItemName(targetType, memberMetadata);
        foreach (var child in element.Elements().Where(node => node.Name.LocalName == itemName))
        {
            list.Add(ReadValue(child, itemType, null));
        }

        return GetPrimaryConstructor(targetType).Invoke([list]);
    }

    private static object ReadMap(XElement element, Type targetType, PropertyInfo? memberMetadata)
    {
        var valuesProperty = GetValuesProperty(targetType);
        var valueType = GetMapValueType(valuesProperty.PropertyType);
        var dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(string), valueType);
        var dictionary = (IDictionary)Activator.CreateInstance(dictionaryType)!;
        var entryName = GetCollectionItemName(targetType, memberMetadata);
        foreach (var entry in element.Elements().Where(node => node.Name.LocalName == entryName))
        {
            var key = entry.Elements().FirstOrDefault(node => node.Name.LocalName == "key")?.Value;
            if (key is null)
            {
                continue;
            }

            var valueElement = entry.Elements().FirstOrDefault(node => node.Name.LocalName == "value");
            dictionary.Add(
                key,
                valueElement is null ? null : ReadValue(valueElement, valueType, null)
            );
        }

        return GetPrimaryConstructor(targetType).Invoke([dictionary]);
    }

    private static object ReadUnion(XElement element, Type targetType)
    {
        var members = element.Elements().ToArray();
        if (members.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one member for Smithy union '{targetType.Name}'."
            );
        }

        var member = members[0];
        foreach (var variantType in targetType.GetNestedTypes(BindingFlags.Public))
        {
            var variantMember = variantType.GetCustomAttribute<SmithyMemberAttribute>();
            if (variantMember is null || GetMemberElementName(variantType, variantMember) != member.Name.LocalName)
            {
                continue;
            }

            return Activator.CreateInstance(
                    variantType,
                    ReadValue(member, GetUnionValueType(variantType), null)
                )
                ?? throw new InvalidOperationException(
                    $"Activator returned null for union variant '{variantType.Name}' of '{targetType.Name}'."
                );
        }

        throw new NotSupportedException(
            $"Unknown Smithy union variants are not supported for '{targetType.Name}'."
        );
    }

    private static string FormatScalar(object value)
    {
        return value switch
        {
            bool boolean => boolean ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture)
                ?? string.Empty,
            _ => value.ToString() ?? string.Empty,
        };
    }

    private static string FormatTimestamp(DateTimeOffset value, PropertyInfo? memberMetadata)
    {
        return GetTraitValue(memberMetadata, TimestampFormatTraitId) switch
        {
            "epoch-seconds" => value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            "http-date" => value.ToUniversalTime().ToString("r", CultureInfo.InvariantCulture),
            _ => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        };
    }

    private static DateTimeOffset ParseTimestamp(string value, PropertyInfo? memberMetadata)
    {
        return GetTraitValue(memberMetadata, TimestampFormatTraitId) switch
        {
            "epoch-seconds" => DateTimeOffset.FromUnixTimeSeconds(
                long.Parse(value, CultureInfo.InvariantCulture)
            ),
            "http-date" => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture),
            _ => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture),
        };
    }

    private static bool HasTrait(MemberInfo member, string traitId)
    {
        return member.GetCustomAttributes<SmithyTraitAttribute>().Any(attribute =>
            string.Equals(attribute.Id, traitId, StringComparison.Ordinal)
        );
    }

    private static string? GetTraitValue(MemberInfo? member, string traitId)
    {
        return member?.GetCustomAttributes<SmithyTraitAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Id, traitId, StringComparison.Ordinal))
            ?.Value;
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

    private static string GetMemberElementName(PropertyInfo property)
    {
        var member = property.GetCustomAttribute<SmithyMemberAttribute>()!;
        return GetTraitValue(property, XmlNameTraitId) ?? member.Name;
    }

    private static string GetMemberElementName(Type variantType, SmithyMemberAttribute member)
    {
        return GetTraitValue(variantType, XmlNameTraitId) ?? member.Name;
    }

    private static string GetCollectionItemName(Type type, PropertyInfo? memberMetadata)
    {
        var valuesProperty = GetValuesProperty(type);
        return GetTraitValue(valuesProperty, XmlNameTraitId)
            ?? GetTraitValue(memberMetadata, XmlNameTraitId)
            ?? (GetShape(type)?.Kind == ShapeKind.Map ? "entry" : "member");
    }

    private static bool IsOptionalParameter(Type type)
    {
        return Nullable.GetUnderlyingType(type) is not null || !type.IsValueType;
    }

    private static object CreateMissingValue(Type type)
    {
        throw new InvalidOperationException(
            string.Create(CultureInfo.InvariantCulture, $"XML value is missing for '{type}'.")
        );
    }
}
