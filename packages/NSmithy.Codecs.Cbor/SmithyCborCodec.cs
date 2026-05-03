using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using NSmithy.Core;
using NSmithy.Core.Annotations;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Cbor;

public sealed class SmithyCborCodec : ISmithyCodec
{
    public static SmithyCborCodec Default { get; } = new();

    public string MediaType => "application/cbor";

    public IShapeSerializer CreateSerializer(Stream sink) => new TopLevelSerializer(sink);

    public IShapeDeserializer CreateDeserializer(ReadOnlySequence<byte> source) =>
        new ReflectionDeserializer(source.ToArray());

    private sealed class TopLevelSerializer(Stream sink) : IShapeSerializer
    {
        private readonly Stream sink = sink;
        private bool written;

        public void WriteStruct(Schema schema, ISerializableStruct value) =>
            WriteObject(value, value.GetType());

        public void WriteList<TState>(
            Schema schema,
            TState state,
            int size,
            Action<TState, IShapeSerializer> consumer
        ) => WriteObject(state, state?.GetType() ?? ResolveType(schema));

        public void WriteMap<TState>(
            Schema schema,
            TState state,
            int size,
            Action<TState, IMapSerializer> consumer
        ) => WriteObject(state, state?.GetType() ?? ResolveType(schema));

        public void WriteBoolean(Schema schema, bool value) => WriteObject(value, typeof(bool));

        public void WriteByte(Schema schema, byte value) => WriteObject(value, typeof(byte));

        public void WriteShort(Schema schema, short value) => WriteObject(value, typeof(short));

        public void WriteInteger(Schema schema, int value) => WriteObject(value, typeof(int));

        public void WriteLong(Schema schema, long value) => WriteObject(value, typeof(long));

        public void WriteFloat(Schema schema, float value) => WriteObject(value, typeof(float));

        public void WriteDouble(Schema schema, double value) => WriteObject(value, typeof(double));

        public void WriteBigInteger(Schema schema, System.Numerics.BigInteger value) =>
            WriteObject(value, typeof(System.Numerics.BigInteger));

        public void WriteBigDecimal(Schema schema, decimal value) =>
            WriteObject(value, typeof(decimal));

        public void WriteString(Schema schema, string value) => WriteObject(value, typeof(string));

        public void WriteBlob(Schema schema, ReadOnlySpan<byte> value) =>
            WriteObject(value.ToArray(), typeof(byte[]));

        public void WriteTimestamp(Schema schema, DateTimeOffset value) =>
            WriteObject(value, typeof(DateTimeOffset));

        public void WriteDocument(Schema schema, Document value) => WriteObject(value, typeof(Document));

        public void WriteNull(Schema schema) => WriteObject(null, ResolveType(schema));

        public void Flush() => sink.Flush();

        public void Dispose() { }

        private void WriteObject(object? value, Type declaredType)
        {
            if (written)
            {
                throw new InvalidOperationException("Codec serializer only supports a single top-level value.");
            }

            var content = CborReflectionCodec.SerializeObject(value, declaredType);
            sink.Write(content, 0, content.Length);
            written = true;
        }
    }

    private sealed class ReflectionDeserializer(byte[] content) : IShapeDeserializer
    {
        private readonly byte[] content = content;
        private object? value = RootSentinel.Instance;

        public void ReadStruct<TState>(Schema schema, TState state, StructMemberConsumer<TState> consumer)
        {
            var current = EnsureValue(schema);
            if (schema.Kind == ShapeKind.Union)
            {
                ReadUnion(schema, current, state, consumer);
                return;
            }

            if (current is null)
            {
                return;
            }

            foreach (var member in schema.Members)
            {
                var memberValue = GetStructMemberValue(current, member.MemberName!);
                if (memberValue is MissingMember)
                {
                    continue;
                }

                consumer.Member(state, member, new ReflectionDeserializer(memberValue is NullValue ? [] : content) { value = Unwrap(memberValue) });
            }
        }

        public void ReadList<TState>(Schema schema, TState state, ListMemberConsumer<TState> consumer)
        {
            var current = EnsureValue(schema);
            if (current is not IEnumerable values || current is string)
            {
                return;
            }

            foreach (var item in values)
            {
                consumer(state, new ReflectionDeserializer(item is null ? [] : content) { value = item });
            }
        }

        public void ReadMap<TState>(Schema schema, TState state, MapMemberConsumer<TState> consumer)
        {
            var current = EnsureValue(schema);
            foreach (var item in EnumerateMap(current))
            {
                consumer(state, item.Key, new ReflectionDeserializer(item.Value is null ? [] : content) { value = item.Value });
            }
        }

        public bool ReadBoolean(Schema schema) =>
            Convert.ToBoolean(EnsureValue(schema), CultureInfo.InvariantCulture);

        public byte ReadByte(Schema schema) =>
            Convert.ToByte(EnsureValue(schema), CultureInfo.InvariantCulture);

        public short ReadShort(Schema schema) =>
            Convert.ToInt16(EnsureValue(schema), CultureInfo.InvariantCulture);

        public int ReadInteger(Schema schema) =>
            Convert.ToInt32(EnsureValue(schema), CultureInfo.InvariantCulture);

        public long ReadLong(Schema schema) =>
            Convert.ToInt64(EnsureValue(schema), CultureInfo.InvariantCulture);

        public float ReadFloat(Schema schema) =>
            Convert.ToSingle(EnsureValue(schema), CultureInfo.InvariantCulture);

        public double ReadDouble(Schema schema) =>
            Convert.ToDouble(EnsureValue(schema), CultureInfo.InvariantCulture);

        public System.Numerics.BigInteger ReadBigInteger(Schema schema) =>
            EnsureValue(schema) switch
            {
                System.Numerics.BigInteger bigInteger => bigInteger,
                var scalar => new System.Numerics.BigInteger(Convert.ToInt64(scalar, CultureInfo.InvariantCulture)),
            };

        public decimal ReadBigDecimal(Schema schema) =>
            Convert.ToDecimal(EnsureValue(schema), CultureInfo.InvariantCulture);

        public string ReadString(Schema schema) => (string)EnsureValue(schema)!;

        public byte[] ReadBlob(Schema schema) => (byte[])EnsureValue(schema)!;

        public DateTimeOffset ReadTimestamp(Schema schema) => (DateTimeOffset)EnsureValue(schema)!;

        public Document ReadDocument(Schema schema) => (Document)EnsureValue(schema)!;

        public bool IsNull() => value is null || value is NullValue;

        public void ReadNull() { }

        public int ContainerSize()
        {
            return value switch
            {
                ICollection collection => collection.Count,
                IEnumerable enumerable => enumerable.Cast<object?>().Count(),
                _ => -1,
            };
        }

        public void Dispose() { }

        private object? EnsureValue(Schema schema)
        {
            if (!ReferenceEquals(value, RootSentinel.Instance))
            {
                return value;
            }

            value = CborReflectionCodec.DeserializeObject(content, ResolveType(schema));
            return value;
        }

        private static void ReadUnion<TState>(
            Schema schema,
            object? current,
            TState state,
            StructMemberConsumer<TState> consumer
        )
        {
            if (current is null)
            {
                return;
            }

            var runtimeType = current.GetType();
            if (string.Equals(runtimeType.Name, "Unknown", StringComparison.Ordinal))
            {
                var tag = runtimeType.GetProperty("Tag")?.GetValue(current)?.ToString();
                var rawValue = runtimeType.GetProperty("Value")?.GetValue(current);
                if (tag is not null)
                {
                    consumer.UnknownMember?.Invoke(
                        state,
                        tag,
                        new ReflectionDeserializer(rawValue is null ? [] : Array.Empty<byte>()) { value = rawValue }
                    );
                }

                return;
            }

            var member = runtimeType.GetCustomAttribute<SmithyMemberAttribute>();
            if (member is null)
            {
                return;
            }

            var memberSchema = schema.GetMember(member.Name);
            if (memberSchema is null)
            {
                return;
            }

            var unionValue = runtimeType.GetProperty("Value")?.GetValue(current);
            consumer.Member(
                state,
                memberSchema,
                new ReflectionDeserializer(unionValue is null ? [] : Array.Empty<byte>()) { value = unionValue }
            );
        }

        private static object? GetStructMemberValue(object current, string memberName)
        {
            var property =
                current.GetType()
                    .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(p =>
                        string.Equals(
                            p.GetCustomAttribute<SmithyMemberAttribute>()?.Name ?? p.Name,
                            memberName,
                            StringComparison.Ordinal
                        )
                    );

            if (property is null)
            {
                return MissingMember.Instance;
            }

            return property.GetValue(current) ?? NullValue.Instance;
        }

        private static object? Unwrap(object? value) =>
            value is NullValue or MissingMember ? null : value;
    }

    private static IEnumerable<KeyValuePair<string, object?>> EnumerateMap(object? value)
    {
        if (value is not IEnumerable enumerable)
        {
            yield break;
        }

        foreach (var item in enumerable)
        {
            if (item is null)
            {
                continue;
            }

            if (item is DictionaryEntry entry && entry.Key is not null)
            {
                yield return new(entry.Key.ToString() ?? string.Empty, entry.Value);
                continue;
            }

            var itemType = item.GetType();
            var key = itemType.GetProperty("Key")?.GetValue(item)?.ToString();
            if (key is null)
            {
                continue;
            }

            yield return new(key, itemType.GetProperty("Value")?.GetValue(item));
        }
    }

    private static readonly ConcurrentDictionary<ShapeId, Type> TypeCache = new();

    private static Type ResolveType(Schema schema) =>
        TypeCache.GetOrAdd(
            schema.Id,
            id =>
                AppDomain.CurrentDomain
                    .GetAssemblies()
                    .SelectMany(GetTypes)
                    .FirstOrDefault(type =>
                        type.GetCustomAttribute<SmithyShapeAttribute>() is { } shape
                        && string.Equals(shape.Id, id.ToString(), StringComparison.Ordinal)
                    )
                ?? throw new InvalidOperationException($"Unable to resolve CLR type for shape '{id}'.")
        );

    private static IEnumerable<Type> GetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException error)
        {
            return error.Types.Where(type => type is not null)!;
        }
    }

    private sealed class RootSentinel
    {
        public static readonly RootSentinel Instance = new();
    }

    private sealed class MissingMember
    {
        public static readonly MissingMember Instance = new();
    }

    private sealed class NullValue
    {
        public static readonly NullValue Instance = new();
    }
}
