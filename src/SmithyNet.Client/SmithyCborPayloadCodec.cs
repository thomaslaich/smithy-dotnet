using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using SmithyNet.Core;
using SmithyNet.Core.Annotations;

namespace SmithyNet.Client;

public sealed class SmithyCborPayloadCodec : ISmithyPayloadCodec
{
    public static SmithyCborPayloadCodec Default { get; } = new();

    public string MediaType => "application/cbor";

    public byte[] Serialize<T>(T value)
    {
        using var writer = new CborBufferWriter();
        WriteValue(writer, value, typeof(T));
        return writer.ToArray();
    }

    public byte[] SerializeMembers(string rootName, IReadOnlyDictionary<string, object?> members)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootName);
        ArgumentNullException.ThrowIfNull(members);

        return Serialize(members);
    }

    public T Deserialize<T>(byte[] content)
    {
        var reader = new CborBufferReader(content);
        var value = reader.ReadValue();
        reader.EnsureFullyConsumed();
        return (T)ConvertValue(value, typeof(T))!;
    }

    public T DeserializeMember<T>(byte[] content, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var reader = new CborBufferReader(content);
        var value = reader.ReadValue();
        reader.EnsureFullyConsumed();
        if (value is not IReadOnlyDictionary<string, object?> map || !map.TryGetValue(name, out var member))
        {
            return default!;
        }

        return (T)ConvertValue(member, typeof(T))!;
    }

    private static void WriteValue(CborBufferWriter writer, object? value, Type declaredType)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        var runtimeType = value.GetType();
        if (value is Document)
        {
            throw new NotSupportedException("Smithy document values are not supported by rpcv2Cbor.");
        }

        if (value is byte[] bytes)
        {
            writer.WriteByteString(bytes);
            return;
        }

        if (value is DateTimeOffset timestamp)
        {
            writer.WriteEpochTimestamp(timestamp);
            return;
        }

        var shape = GetShape(runtimeType) ?? GetShape(declaredType);
        if (shape is not null)
        {
            WriteShape(writer, value, runtimeType, shape);
            return;
        }

        WritePrimitive(writer, value, runtimeType);
    }

    private static void WriteShape(
        CborBufferWriter writer,
        object value,
        Type runtimeType,
        SmithyShapeAttribute shape
    )
    {
        switch (shape.Kind)
        {
            case ShapeKind.Structure:
                WriteStructure(writer, value, runtimeType);
                break;
            case ShapeKind.List:
            case ShapeKind.Set:
                WriteEnumerable(writer, GetValues(value));
                break;
            case ShapeKind.Map:
                WriteMap(writer, GetValues(value));
                break;
            case ShapeKind.Enum:
                writer.WriteTextString(
                    value.GetType().GetProperty("Value")?.GetValue(value) as string
                    ?? throw new NotSupportedException(
                        $"Smithy string enum '{value.GetType()}' is missing a string Value property."
                    )
                );
                break;
            case ShapeKind.IntEnum:
                writer.WriteInt64(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                break;
            case ShapeKind.Union:
                WriteUnion(writer, value, runtimeType);
                break;
            default:
                throw new NotSupportedException(
                    $"Smithy CBOR serialization for shape kind '{shape.Kind}' is not supported."
                );
        }
    }

    private static void WriteStructure(CborBufferWriter writer, object value, Type type)
    {
        var members = new List<(string Name, object? Value, Type DeclaredType)>();
        foreach (var property in GetSmithyProperties(type))
        {
            var member = property.GetCustomAttribute<SmithyMemberAttribute>()!;
            var propertyValue = property.GetValue(value);
            if (propertyValue is null)
            {
                continue;
            }

            members.Add((member.Name, propertyValue, property.PropertyType));
        }

        writer.WriteStartMap(members.Count);
        foreach (var member in members)
        {
            writer.WriteTextString(member.Name);
            WriteValue(writer, member.Value, member.DeclaredType);
        }
    }

    private static void WriteEnumerable(CborBufferWriter writer, object? values)
    {
        var items = values is IEnumerable enumerable ? enumerable.Cast<object?>().ToArray() : [];
        writer.WriteStartArray(items.Length);
        foreach (var item in items)
        {
            WriteValue(writer, item, item?.GetType() ?? typeof(object));
        }
    }

    private static void WriteMap(CborBufferWriter writer, object? values)
    {
        var items = new List<KeyValuePair<string, object?>>();
        if (values is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item is null)
                {
                    continue;
                }

                if (item is DictionaryEntry entry && entry.Key is not null)
                {
                    items.Add(new KeyValuePair<string, object?>(entry.Key.ToString()!, entry.Value));
                    continue;
                }

                var itemType = item.GetType();
                var key = itemType.GetProperty("Key")?.GetValue(item)?.ToString();
                if (key is null)
                {
                    continue;
                }

                items.Add(
                    new KeyValuePair<string, object?>(
                        key,
                        itemType.GetProperty("Value")?.GetValue(item)
                    )
                );
            }
        }

        writer.WriteStartMap(items.Count);
        foreach (var item in items)
        {
            writer.WriteTextString(item.Key);
            WriteValue(writer, item.Value, item.Value?.GetType() ?? typeof(object));
        }
    }

    private static void WriteUnion(CborBufferWriter writer, object value, Type runtimeType)
    {
        if (string.Equals(runtimeType.Name, "Unknown", StringComparison.Ordinal))
        {
            throw new NotSupportedException("Unknown union variants are not supported by rpcv2Cbor.");
        }

        var member =
            runtimeType.GetCustomAttribute<SmithyMemberAttribute>()
            ?? throw new NotSupportedException(
                $"Union variant '{runtimeType}' is missing Smithy member metadata."
            );
        var variantValue = runtimeType.GetProperty("Value")?.GetValue(value);
        writer.WriteStartMap(1);
        writer.WriteTextString(member.Name);
        WriteValue(writer, variantValue, GetUnionValueType(runtimeType));
    }

    private static void WritePrimitive(CborBufferWriter writer, object value, Type type)
    {
        switch (Type.GetTypeCode(type))
        {
            case TypeCode.Boolean:
                writer.WriteBoolean((bool)value);
                break;
            case TypeCode.SByte:
            case TypeCode.Byte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
            case TypeCode.UInt32:
            case TypeCode.Int64:
            case TypeCode.UInt64:
                writer.WriteInt64(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                break;
            case TypeCode.Single:
                writer.WriteSingle((float)value);
                break;
            case TypeCode.Double:
                writer.WriteDouble((double)value);
                break;
            case TypeCode.String:
                writer.WriteTextString((string)value);
                break;
            default:
                throw new NotSupportedException(
                    $"Smithy CBOR serialization for '{type}' is not supported."
                );
        }
    }

    private static object? ConvertValue(object? value, Type targetType)
    {
        var nullableType = Nullable.GetUnderlyingType(targetType);
        targetType = nullableType ?? targetType;
        if (value is null)
        {
            return nullableType is not null || !targetType.IsValueType
                ? null
                : CreateMissingValue(targetType);
        }

        if (targetType == typeof(Document))
        {
            throw new NotSupportedException("Smithy document values are not supported by rpcv2Cbor.");
        }

        if (targetType == typeof(byte[]))
        {
            return value switch
            {
                byte[] bytes => bytes,
                _ => throw new InvalidOperationException($"CBOR value cannot be assigned to '{targetType}'."),
            };
        }

        if (targetType == typeof(DateTimeOffset))
        {
            return value switch
            {
                DateTimeOffset timestamp => timestamp,
                long seconds => DateTimeOffset.FromUnixTimeSeconds(seconds),
                double epochSeconds => DateTimeOffset.UnixEpoch.AddSeconds(epochSeconds),
                _ => throw new InvalidOperationException($"CBOR value cannot be assigned to '{targetType}'."),
            };
        }

        var shape = GetShape(targetType);
        if (shape is not null)
        {
            return ReadShape(value, targetType, shape);
        }

        if (targetType.IsEnum)
        {
            return Enum.ToObject(targetType, Convert.ToInt32(value, CultureInfo.InvariantCulture));
        }

        return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }

    private static object? ReadShape(object value, Type targetType, SmithyShapeAttribute shape)
    {
        return shape.Kind switch
        {
            ShapeKind.Structure => ReadStructure(value, targetType),
            ShapeKind.List or ShapeKind.Set => ReadList(value, targetType),
            ShapeKind.Map => ReadMap(value, targetType),
            ShapeKind.Enum => Activator.CreateInstance(targetType, Convert.ToString(value, CultureInfo.InvariantCulture)),
            ShapeKind.IntEnum => Enum.ToObject(targetType, Convert.ToInt32(value, CultureInfo.InvariantCulture)),
            ShapeKind.Union => ReadUnion(value, targetType),
            _ => throw new NotSupportedException(
                $"Smithy CBOR deserialization for shape kind '{shape.Kind}' is not supported."
            ),
        };
    }

    private static object ReadStructure(object value, Type targetType)
    {
        if (value is not IReadOnlyDictionary<string, object?> members)
        {
            throw new InvalidOperationException($"Expected CBOR map for Smithy structure '{targetType}'.");
        }

        var constructor = GetPrimaryConstructor(targetType);
        var properties = GetSmithyProperties(targetType);
        var arguments = new object?[constructor.GetParameters().Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            var parameter = constructor.GetParameters()[i];
            if (string.Equals(parameter.Name, "message", StringComparison.OrdinalIgnoreCase))
            {
                members.TryGetValue("message", out var messageValue);
                arguments[i] = ConvertValue(messageValue, parameter.ParameterType);
                continue;
            }

            var property = FindConstructorProperty(properties, parameter);
            var member = property.GetCustomAttribute<SmithyMemberAttribute>()!;
            members.TryGetValue(member.Name, out var memberValue);
            arguments[i] = ConvertValue(memberValue, parameter.ParameterType);
        }

        return constructor.Invoke(arguments);
    }

    private static object ReadList(object value, Type targetType)
    {
        if (value is not IReadOnlyList<object?> items)
        {
            throw new InvalidOperationException($"Expected CBOR array for Smithy list '{targetType}'.");
        }

        var valuesProperty = GetValuesProperty(targetType);
        var itemType = GetEnumerableItemType(valuesProperty.PropertyType);
        var listType = typeof(List<>).MakeGenericType(itemType);
        var list = (IList)Activator.CreateInstance(listType)!;
        foreach (var item in items)
        {
            list.Add(ConvertValue(item, itemType));
        }

        return GetPrimaryConstructor(targetType).Invoke([list]);
    }

    private static object ReadMap(object value, Type targetType)
    {
        if (value is not IReadOnlyDictionary<string, object?> entries)
        {
            throw new InvalidOperationException($"Expected CBOR map for Smithy map '{targetType}'.");
        }

        var valuesProperty = GetValuesProperty(targetType);
        var valueType = GetMapValueType(valuesProperty.PropertyType);
        var dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(string), valueType);
        var dictionary = (IDictionary)Activator.CreateInstance(dictionaryType)!;
        foreach (var entry in entries)
        {
            dictionary.Add(entry.Key, ConvertValue(entry.Value, valueType));
        }

        return GetPrimaryConstructor(targetType).Invoke([dictionary]);
    }

    private static object ReadUnion(object value, Type targetType)
    {
        if (value is not IReadOnlyDictionary<string, object?> members)
        {
            throw new InvalidOperationException($"Expected CBOR map for Smithy union '{targetType}'.");
        }

        foreach (var variantType in targetType.GetNestedTypes(BindingFlags.Public))
        {
            var member = variantType.GetCustomAttribute<SmithyMemberAttribute>();
            if (member is null || !members.TryGetValue(member.Name, out var memberValue))
            {
                continue;
            }

            return Activator.CreateInstance(
                    variantType,
                    ConvertValue(memberValue, GetUnionValueType(variantType))
                )
                ?? throw new InvalidOperationException(
                    $"Activator returned null for union variant '{variantType.Name}' of '{targetType.Name}'."
                );
        }

        throw new NotSupportedException(
            $"Unknown Smithy union variants are not supported for '{targetType.Name}'."
        );
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

    private static object CreateMissingValue(Type type)
    {
        throw new InvalidOperationException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"CBOR null cannot be assigned to '{type}'."
            )
        );
    }

    private sealed class CborBufferWriter : IDisposable
    {
        private readonly MemoryStream stream = new();

        public byte[] ToArray() => stream.ToArray();

        public void Dispose()
        {
            stream.Dispose();
        }

        public void WriteNull() => stream.WriteByte(0xf6);

        public void WriteBoolean(bool value) => stream.WriteByte(value ? (byte)0xf5 : (byte)0xf4);

        public void WriteByteString(byte[] value)
        {
            WriteLength(2, (ulong)value.Length);
            stream.Write(value, 0, value.Length);
        }

        public void WriteTextString(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            WriteLength(3, (ulong)bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        public void WriteStartArray(int count) => WriteLength(4, (ulong)count);

        public void WriteStartMap(int count) => WriteLength(5, (ulong)count);

        public void WriteInt64(long value)
        {
            if (value >= 0)
            {
                WriteLength(0, (ulong)value);
            }
            else
            {
                WriteLength(1, (ulong)(-1 - value));
            }
        }

        public void WriteSingle(float value)
        {
            stream.WriteByte(0xfa);
            WriteBigEndian(BitConverter.SingleToInt32Bits(value));
        }

        public void WriteDouble(double value)
        {
            stream.WriteByte(0xfb);
            WriteBigEndian(BitConverter.DoubleToInt64Bits(value));
        }

        public void WriteEpochTimestamp(DateTimeOffset value)
        {
            WriteTag(1);
            var unixMilliseconds = value.ToUnixTimeMilliseconds();
            if (unixMilliseconds % 1000 == 0)
            {
                WriteInt64(unixMilliseconds / 1000);
            }
            else
            {
                WriteDouble(unixMilliseconds / 1000d);
            }
        }

        private void WriteTag(ulong tag) => WriteLength(6, tag);

        private void WriteLength(int majorType, ulong value)
        {
            if (value <= 23)
            {
                stream.WriteByte((byte)((majorType << 5) | (byte)value));
                return;
            }

            if (value <= byte.MaxValue)
            {
                stream.WriteByte((byte)((majorType << 5) | 24));
                stream.WriteByte((byte)value);
                return;
            }

            if (value <= ushort.MaxValue)
            {
                stream.WriteByte((byte)((majorType << 5) | 25));
                WriteBigEndian((ushort)value);
                return;
            }

            if (value <= uint.MaxValue)
            {
                stream.WriteByte((byte)((majorType << 5) | 26));
                WriteBigEndian((uint)value);
                return;
            }

            stream.WriteByte((byte)((majorType << 5) | 27));
            WriteBigEndian(value);
        }

        private void WriteBigEndian(short value) => WriteBigEndian((ushort)value);

        private void WriteBigEndian(ushort value)
        {
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }

        private void WriteBigEndian(int value) => WriteBigEndian((uint)value);

        private void WriteBigEndian(uint value)
        {
            stream.WriteByte((byte)(value >> 24));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }

        private void WriteBigEndian(long value) => WriteBigEndian((ulong)value);

        private void WriteBigEndian(ulong value)
        {
            for (var shift = 56; shift >= 0; shift -= 8)
            {
                stream.WriteByte((byte)(value >> shift));
            }
        }
    }

    private sealed class CborBufferReader(byte[] buffer)
    {
        private int offset;

        public void EnsureFullyConsumed()
        {
            if (offset != buffer.Length)
            {
                throw new InvalidOperationException("CBOR payload contains trailing data.");
            }
        }

        public object? ReadValue()
        {
            var initial = ReadByte();
            var majorType = initial >> 5;
            var additionalInfo = (byte)(initial & 0x1f);
            return majorType switch
            {
                0 => (long)ReadUnsigned(additionalInfo),
                1 => -1L - (long)ReadUnsigned(additionalInfo),
                2 => ReadByteString(ReadUnsigned(additionalInfo)),
                3 => ReadTextString(ReadUnsigned(additionalInfo)),
                4 => ReadArray(ReadUnsigned(additionalInfo)),
                5 => ReadMap(ReadUnsigned(additionalInfo)),
                6 => ReadTaggedValue(ReadUnsigned(additionalInfo)),
                7 => ReadSimple(additionalInfo),
                _ => throw new InvalidOperationException($"Unsupported CBOR major type '{majorType}'."),
            };
        }

        private object? ReadTaggedValue(ulong tag)
        {
            var value = ReadValue();
            return tag switch
            {
                1 when value is long seconds => DateTimeOffset.FromUnixTimeSeconds(seconds),
                1 when value is double epochSeconds => DateTimeOffset.UnixEpoch.AddSeconds(epochSeconds),
                2 or 3 or 4 => throw new NotSupportedException(
                    "rpcv2Cbor big integer and big decimal values are not supported."
                ),
                _ => value,
            };
        }

        private object? ReadSimple(byte additionalInfo)
        {
            return additionalInfo switch
            {
                20 => false,
                21 => true,
                22 => null,
                23 => null,
                25 => (double)(float)BitConverter.UInt16BitsToHalf(ReadUInt16()),
                26 => (double)BitConverter.Int32BitsToSingle((int)ReadUInt32()),
                27 => BitConverter.Int64BitsToDouble((long)ReadUInt64()),
                _ => throw new InvalidOperationException(
                    $"Unsupported CBOR simple value '{additionalInfo}'."
                ),
            };
        }

        private byte[] ReadByteString(ulong length)
        {
            var bytes = new byte[length];
            Array.Copy(buffer, offset, bytes, 0, (int)length);
            offset += (int)length;
            return bytes;
        }

        private string ReadTextString(ulong length)
        {
            var text = Encoding.UTF8.GetString(buffer, offset, (int)length);
            offset += (int)length;
            return text;
        }

        private List<object?> ReadArray(ulong length)
        {
            var items = new List<object?>((int)length);
            for (ulong i = 0; i < length; i++)
            {
                items.Add(ReadValue());
            }

            return items;
        }

        private Dictionary<string, object?> ReadMap(ulong length)
        {
            var values = new Dictionary<string, object?>((int)length, StringComparer.Ordinal);
            for (ulong i = 0; i < length; i++)
            {
                var key = ReadValue() as string
                    ?? throw new InvalidOperationException("CBOR map keys must be strings.");
                values[key] = ReadValue();
            }

            return values;
        }

        private ulong ReadUnsigned(byte additionalInfo)
        {
            return additionalInfo switch
            {
                < 24 => additionalInfo,
                24 => ReadByte(),
                25 => ReadUInt16(),
                26 => ReadUInt32(),
                27 => ReadUInt64(),
                _ => throw new InvalidOperationException(
                    $"Unsupported CBOR additional info '{additionalInfo}'."
                ),
            };
        }

        private byte ReadByte()
        {
            if (offset >= buffer.Length)
            {
                throw new InvalidOperationException("Unexpected end of CBOR payload.");
            }

            return buffer[offset++];
        }

        private ushort ReadUInt16()
        {
            var value = (ushort)((ReadByte() << 8) | ReadByte());
            return value;
        }

        private uint ReadUInt32()
        {
            return ((uint)ReadByte() << 24)
                | ((uint)ReadByte() << 16)
                | ((uint)ReadByte() << 8)
                | ReadByte();
        }

        private ulong ReadUInt64()
        {
            return ((ulong)ReadByte() << 56)
                | ((ulong)ReadByte() << 48)
                | ((ulong)ReadByte() << 40)
                | ((ulong)ReadByte() << 32)
                | ((ulong)ReadByte() << 24)
                | ((ulong)ReadByte() << 16)
                | ((ulong)ReadByte() << 8)
                | ReadByte();
        }
    }
}
