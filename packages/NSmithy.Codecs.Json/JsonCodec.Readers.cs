using System.Globalization;
using System.Numerics;
using System.Text.Json;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Json;

public static partial class JsonCodec
{
    private interface IJsonValueReader<T>
    {
        T Read(JsonElement value);
    }

    private interface IJsonMemberReader<in TBuilder>
    {
        string Name { get; }

        bool IsRequired { get; }

        void ReadMissing(TBuilder builder);

        void ReadInto(TBuilder builder, JsonElement value);
    }

    private interface IJsonUnionCaseReader<out TUnion>
    {
        string Name { get; }

        TUnion Read(JsonElement value);
    }

    private sealed class JsonReaderCompiler
    {
        private readonly Dictionary<Schema, object> cache = new(ReferenceEqualityComparer.Instance);

        public static IJsonValueReader<T> Compile<T>(Schema<T> schema)
        {
            ArgumentNullException.ThrowIfNull(schema);
            return new JsonReaderCompiler().CompileValue(schema);
        }

        public IJsonValueReader<T> CompileValue<T>(Schema<T> schema)
        {
            var resolved = schema.Resolved;
            if (cache.TryGetValue(resolved, out var cached))
            {
                return (IJsonValueReader<T>)cached;
            }

            var deferred = new DeferredJsonValueReader<T>();
            cache.Add(resolved, deferred);
            deferred.Set(CompileValueCore(schema, resolved));
            return deferred;
        }

        private IJsonValueReader<T> CompileValueCore<T>(Schema<T> schema, Schema resolved)
        {
            if (resolved is INullableSchema)
            {
                return (IJsonValueReader<T>)CompileNullable((dynamic)resolved);
            }

            return resolved.Kind switch
            {
                ShapeKind.Boolean => Cast<T>(new BooleanJsonValueReader()),
                ShapeKind.Byte => Cast<T>(new ByteJsonValueReader()),
                ShapeKind.Short => Cast<T>(new ShortJsonValueReader()),
                ShapeKind.Integer => Cast<T>(new IntegerJsonValueReader()),
                ShapeKind.Long => Cast<T>(new LongJsonValueReader()),
                ShapeKind.Float => Cast<T>(new FloatJsonValueReader()),
                ShapeKind.Double => Cast<T>(new DoubleJsonValueReader()),
                ShapeKind.BigInteger => Cast<T>(new BigIntegerJsonValueReader()),
                ShapeKind.BigDecimal => Cast<T>(new BigDecimalJsonValueReader()),
                ShapeKind.String => Cast<T>(new StringJsonValueReader()),
                ShapeKind.Enum => (IJsonValueReader<T>)CompileStringEnum((dynamic)resolved),
                ShapeKind.IntEnum => (IJsonValueReader<T>)CompileIntEnum((dynamic)resolved),
                ShapeKind.Blob => Cast<T>(new BlobJsonValueReader()),
                ShapeKind.Timestamp => Cast<T>(
                    new TimestampJsonValueReader(TimestampFormat.Resolve(null, resolved))
                ),
                ShapeKind.Document => Cast<T>(new DocumentJsonValueReader()),
                ShapeKind.Structure => (IJsonValueReader<T>)CompileStructure((dynamic)resolved),
                ShapeKind.Union => (IJsonValueReader<T>)CompileUnion((dynamic)resolved),
                ShapeKind.List or ShapeKind.Set => (IJsonValueReader<T>)
                    CompileList((dynamic)resolved),
                ShapeKind.Map => (IJsonValueReader<T>)CompileMap((dynamic)resolved),
                _ => throw new NotSupportedException(
                    $"JSON codec does not support schema kind '{schema.Kind}'."
                ),
            };
        }

        private NullableJsonValueReader<T> CompileNullable<T>(NullableSchema<T> schema)
            where T : struct => new(CompileValue(schema.TargetSchema));

        private static StringEnumJsonValueReader<T> CompileStringEnum<T>(StringEnumSchema<T> schema)
            where T : IStringEnumValue<T> => new(schema);

        private static IntEnumJsonValueReader<T> CompileIntEnum<T>(IntEnumSchema<T> schema)
            where T : struct, Enum => new(schema);

        private StructureJsonValueReader<T, TBuilder> CompileStructure<T, TBuilder>(
            IStructSchema<T, TBuilder> schema
        )
        {
            var visitor = new JsonMemberReaderCompiler<T, TBuilder>(this);
            schema.VisitMembers(visitor);
            return new StructureJsonValueReader<T, TBuilder>(
                schema.CreateTypedBuilder,
                schema.Build,
                visitor.Readers
            );
        }

        private ListJsonValueReader<TCollection, TElement, TBuilder> CompileList<
            TCollection,
            TElement,
            TBuilder
        >(IListSchema<TCollection, TElement, TBuilder> schema) =>
            new(schema, CompileValue(schema.ElementSchema));

        private MapJsonValueReader<TDictionary, TValue, TBuilder> CompileMap<
            TDictionary,
            TValue,
            TBuilder
        >(IMapSchema<TDictionary, TValue, TBuilder> schema) =>
            new(schema, CompileValue(schema.ValueSchema));

        private IJsonValueReader<T> CompileUnion<T>(IUnionSchema<T> schema)
        {
            if (IsOpenUnion(schema))
            {
                return new DelegatingJsonValueReader<T>(value => (T)ReadUnion(schema, value));
            }

            var visitor = new JsonUnionCaseReaderCompiler<T>(this);
            schema.VisitCases(visitor);
            return new UnionJsonValueReader<T>(visitor.Readers);
        }

        private static IJsonValueReader<T> Cast<T>(object reader) => (IJsonValueReader<T>)reader;
    }

    private sealed class DeferredJsonValueReader<T> : IJsonValueReader<T>
    {
        private IJsonValueReader<T>? inner;

        public void Set(IJsonValueReader<T> reader)
        {
            ArgumentNullException.ThrowIfNull(reader);
            inner = reader;
        }

        public T Read(JsonElement value)
        {
            if (inner is null)
            {
                throw new InvalidOperationException("JSON reader has not been initialized.");
            }

            return inner.Read(value);
        }
    }

    private sealed class DelegatingJsonValueReader<T>(Func<JsonElement, T> read)
        : IJsonValueReader<T>
    {
        public T Read(JsonElement value) => read(value);
    }

    private sealed class JsonMemberReaderCompiler<TContainer, TBuilder>(JsonReaderCompiler compiler)
        : IMemberVisitor<TContainer, TBuilder>
    {
        private readonly List<IJsonMemberReader<TBuilder>> readers = [];

        public IReadOnlyList<IJsonMemberReader<TBuilder>> Readers => readers;

        public void Visit<TValue>(IMemberSchema<TContainer, TBuilder, TValue> member)
        {
            readers.Add(
                new JsonMemberReader<TContainer, TBuilder, TValue>(
                    member,
                    compiler.CompileValue(member.TargetSchema)
                )
            );
        }
    }

    private sealed class JsonMemberReader<TContainer, TBuilder, TValue>(
        IMemberSchema<TContainer, TBuilder, TValue> member,
        IJsonValueReader<TValue> valueReader
    ) : IJsonMemberReader<TBuilder>
    {
        public string Name => WireName(member.Traits, member.Name);

        public bool IsRequired => member.IsRequired;

        public void ReadMissing(TBuilder builder)
        {
            if (TryCreateDefaultValue(member.TargetSchema, member.Traits, out var defaultValue))
            {
                member.SetValue(builder, (TValue)defaultValue!);
            }
        }

        public void ReadInto(TBuilder builder, JsonElement value)
        {
            member.SetValue(builder, valueReader.Read(value));
        }
    }

    private sealed class StructureJsonValueReader<T, TBuilder>(
        Func<TBuilder> createBuilder,
        Func<TBuilder, T> build,
        IReadOnlyList<IJsonMemberReader<TBuilder>> memberReaders
    ) : IJsonValueReader<T>
    {
        public T Read(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    $"Expected JSON object but found {value.ValueKind}."
                );
            }

            var builder = createBuilder();
            foreach (var memberReader in memberReaders)
            {
                if (!value.TryGetProperty(memberReader.Name, out var memberValue))
                {
                    if (memberReader.IsRequired)
                    {
                        throw new InvalidOperationException(
                            $"Missing required member '{memberReader.Name}'."
                        );
                    }

                    memberReader.ReadMissing(builder);
                    continue;
                }

                if (memberValue.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                {
                    if (memberReader.IsRequired)
                    {
                        throw new InvalidOperationException(
                            $"Required member '{memberReader.Name}' cannot be null."
                        );
                    }

                    continue;
                }

                memberReader.ReadInto(builder, memberValue);
            }

            return build(builder);
        }
    }

    private sealed class JsonUnionCaseReaderCompiler<TUnion>(JsonReaderCompiler compiler)
        : IUnionCaseVisitor<TUnion>
    {
        private readonly List<IJsonUnionCaseReader<TUnion>> readers = [];

        public IReadOnlyList<IJsonUnionCaseReader<TUnion>> Readers => readers;

        public void Visit<TValue>(IUnionCaseSchema<TUnion, TValue> unionCase)
        {
            readers.Add(
                new JsonUnionCaseReader<TUnion, TValue>(
                    unionCase,
                    compiler.CompileValue(unionCase.TargetSchema)
                )
            );
        }
    }

    private sealed class JsonUnionCaseReader<TUnion, TValue>(
        IUnionCaseSchema<TUnion, TValue> unionCase,
        IJsonValueReader<TValue> valueReader
    ) : IJsonUnionCaseReader<TUnion>
    {
        public string Name => WireName(unionCase.Traits, unionCase.Name);

        public TUnion Read(JsonElement value) => unionCase.Create(valueReader.Read(value));
    }

    private sealed class UnionJsonValueReader<T> : IJsonValueReader<T>
    {
        private readonly Dictionary<string, IJsonUnionCaseReader<T>> readersByName;

        public UnionJsonValueReader(IReadOnlyList<IJsonUnionCaseReader<T>> readers)
        {
            readersByName = readers.ToDictionary(reader => reader.Name, StringComparer.Ordinal);
        }

        public T Read(JsonElement value)
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
                    "Expected union value to contain exactly one member but found "
                        + $"{properties.Length}."
                );
            }

            var property = properties[0];
            if (!readersByName.TryGetValue(property.Name, out var reader))
            {
                throw new InvalidOperationException($"Unknown union member '{property.Name}'.");
            }

            return reader.Read(property.Value);
        }
    }

    private sealed class ListJsonValueReader<TCollection, TElement, TBuilder>(
        IListSchema<TCollection, TElement, TBuilder> schema,
        IJsonValueReader<TElement> elementReader
    ) : IJsonValueReader<TCollection>
    {
        public TCollection Read(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    $"Expected JSON array but found {value.ValueKind}."
                );
            }

            var builder = schema.CreateTypedBuilder();
            foreach (var element in value.EnumerateArray())
            {
                schema.Add(builder, elementReader.Read(element));
            }

            return schema.Build(builder);
        }
    }

    private sealed class MapJsonValueReader<TDictionary, TValue, TBuilder>(
        IMapSchema<TDictionary, TValue, TBuilder> schema,
        IJsonValueReader<TValue> valueReader
    ) : IJsonValueReader<TDictionary>
    {
        public TDictionary Read(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    $"Expected JSON object but found {value.ValueKind}."
                );
            }

            var builder = schema.CreateTypedBuilder();
            foreach (var property in value.EnumerateObject())
            {
                schema.Add(builder, property.Name, valueReader.Read(property.Value));
            }

            return schema.Build(builder);
        }
    }

    private sealed class NullableJsonValueReader<T>(IJsonValueReader<T> inner)
        : IJsonValueReader<T?>
        where T : struct
    {
        public T? Read(JsonElement value) =>
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                ? null
                : inner.Read(value);
    }

    private sealed class BooleanJsonValueReader : IJsonValueReader<bool>
    {
        public bool Read(JsonElement value) => value.GetBoolean();
    }

    private sealed class ByteJsonValueReader : IJsonValueReader<sbyte>
    {
        public sbyte Read(JsonElement value) => value.GetSByte();
    }

    private sealed class ShortJsonValueReader : IJsonValueReader<short>
    {
        public short Read(JsonElement value) => value.GetInt16();
    }

    private sealed class IntegerJsonValueReader : IJsonValueReader<int>
    {
        public int Read(JsonElement value) => value.GetInt32();
    }

    private sealed class LongJsonValueReader : IJsonValueReader<long>
    {
        public long Read(JsonElement value) => value.GetInt64();
    }

    private sealed class FloatJsonValueReader : IJsonValueReader<float>
    {
        public float Read(JsonElement value) => ReadFloat(value);
    }

    private sealed class DoubleJsonValueReader : IJsonValueReader<double>
    {
        public double Read(JsonElement value) => ReadDouble(value);
    }

    private sealed class BigIntegerJsonValueReader : IJsonValueReader<BigInteger>
    {
        public BigInteger Read(JsonElement value) =>
            BigInteger.Parse(value.GetRawText(), CultureInfo.InvariantCulture);
    }

    private sealed class BigDecimalJsonValueReader : IJsonValueReader<decimal>
    {
        public decimal Read(JsonElement value) => value.GetDecimal();
    }

    private sealed class StringJsonValueReader : IJsonValueReader<string>
    {
        public string Read(JsonElement value) =>
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                ? null!
                : value.GetString()!;
    }

    private sealed class StringEnumJsonValueReader<T>(StringEnumSchema<T> schema)
        : IJsonValueReader<T>
        where T : IStringEnumValue<T>
    {
        public T Read(JsonElement value) => schema.Create(value.GetString()!);
    }

    private sealed class IntEnumJsonValueReader<T>(IntEnumSchema<T> schema) : IJsonValueReader<T>
        where T : struct, Enum
    {
        public T Read(JsonElement value) => schema.Create(value.GetInt32());
    }

    private sealed class BlobJsonValueReader : IJsonValueReader<byte[]>
    {
        public byte[] Read(JsonElement value) => value.GetBytesFromBase64();
    }

    private sealed class TimestampJsonValueReader(string format) : IJsonValueReader<DateTimeOffset>
    {
        public DateTimeOffset Read(JsonElement value) => TimestampFormat.Read(value, format);
    }

    private sealed class DocumentJsonValueReader : IJsonValueReader<Document>
    {
        public Document Read(JsonElement value) => Document.FromJsonElement(value);
    }
}
