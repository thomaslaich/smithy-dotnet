using System.Globalization;
using System.Numerics;
using System.Text.Json;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Json;

public interface IFunctionalJsonCodec<T> : IFunctionalCodec<T> { }

public static class FunctionalJsonCodec
{
    public static IFunctionalJsonCodec<T> FromSchema<T>(FunctionalSchema<T> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new CompiledFunctionalJsonCodec<T>(schema);
    }

    public static IFunctionalProjectionCodec<T> FromProjection<T>(
        FunctionalStructProjection<T> projection,
        bool materializeTopLevelDefaults = true
    )
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new CompiledFunctionalJsonProjectionCodec<T>(
            projection,
            materializeTopLevelDefaults
        );
    }

    private sealed class CompiledFunctionalJsonCodec<T>(FunctionalSchema<T> schema)
        : IFunctionalJsonCodec<T>
    {
        private readonly IJsonValueWriter<T> valueWriter = JsonWriterCompiler.Compile(schema);
        private readonly IJsonValueReader<T> valueReader = JsonReaderCompiler.Compile(schema);

        public byte[] Serialize(T value)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                valueWriter.Write(writer, value);
            }

            return stream.ToArray();
        }

        public T Deserialize(byte[] payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            using var document = JsonDocument.Parse(payload);
            return valueReader.Read(document.RootElement);
        }
    }

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
        private readonly Dictionary<FunctionalSchema, object> cache = new(
            ReferenceEqualityComparer.Instance
        );

        public static IJsonValueReader<T> Compile<T>(FunctionalSchema<T> schema)
        {
            ArgumentNullException.ThrowIfNull(schema);
            return new JsonReaderCompiler().CompileValue(schema);
        }

        public IJsonValueReader<T> CompileValue<T>(FunctionalSchema<T> schema)
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

        private IJsonValueReader<T> CompileValueCore<T>(
            FunctionalSchema<T> schema,
            FunctionalSchema resolved
        )
        {
            if (resolved is IFunctionalNullableSchema)
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

        private NullableJsonValueReader<T> CompileNullable<T>(FunctionalNullableSchema<T> schema)
            where T : struct => new(CompileValue(schema.TargetSchema));

        private static StringEnumJsonValueReader<T> CompileStringEnum<T>(
            FunctionalStringEnumSchema<T> schema
        )
            where T : IFunctionalStringEnumValue<T> => new(schema);

        private static IntEnumJsonValueReader<T> CompileIntEnum<T>(
            FunctionalIntEnumSchema<T> schema
        )
            where T : struct, Enum => new(schema);

        private StructureJsonValueReader<T, TBuilder> CompileStructure<T, TBuilder>(
            IFunctionalStructSchema<T, TBuilder> schema
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
        >(IFunctionalListSchema<TCollection, TElement, TBuilder> schema) =>
            new(schema, CompileValue(schema.ElementSchema));

        private MapJsonValueReader<TDictionary, TValue, TBuilder> CompileMap<
            TDictionary,
            TValue,
            TBuilder
        >(IFunctionalMapSchema<TDictionary, TValue, TBuilder> schema) =>
            new(schema, CompileValue(schema.ValueSchema));

        private IJsonValueReader<T> CompileUnion<T>(IFunctionalUnionSchema<T> schema)
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
        : IFunctionalMemberVisitor<TContainer, TBuilder>
    {
        private readonly List<IJsonMemberReader<TBuilder>> readers = [];

        public IReadOnlyList<IJsonMemberReader<TBuilder>> Readers => readers;

        public void Visit<TValue>(IFunctionalMemberSchema<TContainer, TBuilder, TValue> member)
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
        IFunctionalMemberSchema<TContainer, TBuilder, TValue> member,
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
        : IFunctionalUnionCaseVisitor<TUnion>
    {
        private readonly List<IJsonUnionCaseReader<TUnion>> readers = [];

        public IReadOnlyList<IJsonUnionCaseReader<TUnion>> Readers => readers;

        public void Visit<TValue>(IFunctionalUnionCaseSchema<TUnion, TValue> unionCase)
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
        IFunctionalUnionCaseSchema<TUnion, TValue> unionCase,
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
        IFunctionalListSchema<TCollection, TElement, TBuilder> schema,
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
        IFunctionalMapSchema<TDictionary, TValue, TBuilder> schema,
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

    private sealed class StringEnumJsonValueReader<T>(FunctionalStringEnumSchema<T> schema)
        : IJsonValueReader<T>
        where T : IFunctionalStringEnumValue<T>
    {
        public T Read(JsonElement value) => schema.Create(value.GetString()!);
    }

    private sealed class IntEnumJsonValueReader<T>(FunctionalIntEnumSchema<T> schema)
        : IJsonValueReader<T>
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

    private sealed class CompiledFunctionalJsonProjectionCodec<T>(
        FunctionalStructProjection<T> projection,
        bool materializeTopLevelDefaults
    ) : IFunctionalProjectionCodec<T>
    {
        private readonly StructureJsonValueWriter<T> valueWriter = JsonWriterCompiler.Compile(
            projection,
            materializeTopLevelDefaults
        );

        public byte[] Serialize(T value)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                valueWriter.Write(writer, value);
            }

            return stream.ToArray();
        }

        public void ReadInto(byte[] payload, object builder)
        {
            ArgumentNullException.ThrowIfNull(payload);
            ArgumentNullException.ThrowIfNull(builder);

            using var document = JsonDocument.Parse(payload);
            ReadProjectionInto(projection, document.RootElement, builder);
        }
    }

    private interface IJsonValueWriter<in T>
    {
        void Write(Utf8JsonWriter writer, T value);
    }

    private interface IJsonMemberWriter<in TContainer>
    {
        void Write(Utf8JsonWriter writer, TContainer container);
    }

    private interface IJsonUnionCaseWriter<in TUnion>
    {
        bool TryWrite(Utf8JsonWriter writer, TUnion value);
    }

    private sealed class JsonWriterCompiler
    {
        private readonly Dictionary<FunctionalSchema, object> cache = new(
            ReferenceEqualityComparer.Instance
        );

        public static IJsonValueWriter<T> Compile<T>(FunctionalSchema<T> schema)
        {
            ArgumentNullException.ThrowIfNull(schema);
            return new JsonWriterCompiler().CompileValue(schema);
        }

        public static StructureJsonValueWriter<T> Compile<T>(
            FunctionalStructProjection<T> projection,
            bool materializeTopLevelDefaults = true
        )
        {
            ArgumentNullException.ThrowIfNull(projection);
            var compiler = new JsonWriterCompiler();
            return compiler.CompileProjection(projection, materializeTopLevelDefaults);
        }

        public IJsonValueWriter<T> CompileValue<T>(FunctionalSchema<T> schema)
        {
            var resolved = schema.Resolved;
            if (cache.TryGetValue(resolved, out var cached))
            {
                return (IJsonValueWriter<T>)cached;
            }

            var deferred = new DeferredJsonValueWriter<T>();
            cache.Add(resolved, deferred);
            deferred.Set(CompileValueCore(schema, resolved));
            return deferred;
        }

        private IJsonValueWriter<T> CompileValueCore<T>(
            FunctionalSchema<T> schema,
            FunctionalSchema resolved
        )
        {
            if (resolved is IFunctionalNullableSchema)
            {
                return (IJsonValueWriter<T>)CompileNullable((dynamic)resolved);
            }

            return resolved.Kind switch
            {
                ShapeKind.Boolean => Cast<T>(new BooleanJsonValueWriter()),
                ShapeKind.Byte => Cast<T>(new ByteJsonValueWriter()),
                ShapeKind.Short => Cast<T>(new ShortJsonValueWriter()),
                ShapeKind.Integer => Cast<T>(new IntegerJsonValueWriter()),
                ShapeKind.Long => Cast<T>(new LongJsonValueWriter()),
                ShapeKind.Float => Cast<T>(new FloatJsonValueWriter()),
                ShapeKind.Double => Cast<T>(new DoubleJsonValueWriter()),
                ShapeKind.BigInteger => Cast<T>(new BigIntegerJsonValueWriter()),
                ShapeKind.BigDecimal => Cast<T>(new BigDecimalJsonValueWriter()),
                ShapeKind.String => Cast<T>(new StringJsonValueWriter()),
                ShapeKind.Enum => (IJsonValueWriter<T>)CompileStringEnum((dynamic)resolved),
                ShapeKind.IntEnum => (IJsonValueWriter<T>)CompileIntEnum((dynamic)resolved),
                ShapeKind.Blob => Cast<T>(new BlobJsonValueWriter()),
                ShapeKind.Timestamp => Cast<T>(
                    new TimestampJsonValueWriter(TimestampFormat.Resolve(null, resolved))
                ),
                ShapeKind.Document => Cast<T>(new DocumentJsonValueWriter()),
                ShapeKind.Structure when resolved is IFunctionalStructSchema<T> structSchema =>
                    CompileStructure(structSchema),
                ShapeKind.Union when resolved is IFunctionalUnionSchema<T> unionSchema =>
                    CompileUnion(unionSchema),
                ShapeKind.List or ShapeKind.Set => (IJsonValueWriter<T>)
                    CompileList((dynamic)resolved),
                ShapeKind.Map => (IJsonValueWriter<T>)CompileMap((dynamic)resolved),
                _ => throw new NotSupportedException(
                    $"JSON codec does not support schema kind '{schema.Kind}'."
                ),
            };
        }

        private NullableJsonValueWriter<T> CompileNullable<T>(FunctionalNullableSchema<T> schema)
            where T : struct => new NullableJsonValueWriter<T>(CompileValue(schema.TargetSchema));

        private static StringEnumJsonValueWriter<T> CompileStringEnum<T>(
            FunctionalStringEnumSchema<T> schema
        )
            where T : IFunctionalStringEnumValue<T> => new StringEnumJsonValueWriter<T>();

        private static IntEnumJsonValueWriter<T> CompileIntEnum<T>(
            FunctionalIntEnumSchema<T> schema
        )
            where T : struct, Enum => new IntEnumJsonValueWriter<T>(schema);

        private StructureJsonValueWriter<T> CompileStructure<T>(IFunctionalStructSchema<T> schema)
        {
            var visitor = new JsonMemberWriterCompiler<T>(this, materializeDefaults: true);
            schema.VisitMembers(visitor);
            return new StructureJsonValueWriter<T>(visitor.Writers);
        }

        private StructureJsonValueWriter<T> CompileProjection<T>(
            FunctionalStructProjection<T> projection,
            bool materializeTopLevelDefaults
        )
        {
            var visitor = new JsonMemberWriterCompiler<T>(this, materializeTopLevelDefaults);
            projection.VisitMembers(visitor);
            return new StructureJsonValueWriter<T>(visitor.Writers);
        }

        private ListJsonValueWriter<TCollection, TElement> CompileList<TCollection, TElement>(
            IFunctionalListSchema<TCollection, TElement> schema
        ) =>
            new ListJsonValueWriter<TCollection, TElement>(
                schema,
                CompileValue(schema.ElementSchema)
            );

        private MapJsonValueWriter<TDictionary, TValue> CompileMap<TDictionary, TValue>(
            IFunctionalMapSchema<TDictionary, TValue> schema
        ) => new MapJsonValueWriter<TDictionary, TValue>(schema, CompileValue(schema.ValueSchema));

        private IJsonValueWriter<T> CompileUnion<T>(IFunctionalUnionSchema<T> schema)
        {
            if (IsOpenUnion(schema))
            {
                return new DelegatingJsonValueWriter<T>(
                    (writer, value) => WriteUnion(writer, schema, value!)
                );
            }

            var visitor = new JsonUnionCaseWriterCompiler<T>(this);
            schema.VisitCases(visitor);
            return new UnionJsonValueWriter<T>(visitor.Writers);
        }

        private static IJsonValueWriter<T> Cast<T>(object writer) => (IJsonValueWriter<T>)writer;
    }

    private sealed class DeferredJsonValueWriter<T> : IJsonValueWriter<T>
    {
        private IJsonValueWriter<T>? inner;

        public void Set(IJsonValueWriter<T> writer)
        {
            ArgumentNullException.ThrowIfNull(writer);
            inner = writer;
        }

        public void Write(Utf8JsonWriter writer, T value)
        {
            if (inner is null)
            {
                throw new InvalidOperationException("JSON writer has not been initialized.");
            }

            inner.Write(writer, value);
        }
    }

    private sealed class DelegatingJsonValueWriter<T>(Action<Utf8JsonWriter, T> write)
        : IJsonValueWriter<T>
    {
        public void Write(Utf8JsonWriter writer, T value) => write(writer, value);
    }

    private sealed class JsonMemberWriterCompiler<TContainer>(
        JsonWriterCompiler compiler,
        bool materializeDefaults
    ) : IFunctionalMemberVisitor<TContainer>
    {
        private readonly List<IJsonMemberWriter<TContainer>> writers = [];

        public IReadOnlyList<IJsonMemberWriter<TContainer>> Writers => writers;

        public void Visit<TValue>(IFunctionalMemberSchema<TContainer, TValue> member)
        {
            writers.Add(
                new JsonMemberWriter<TContainer, TValue>(
                    member,
                    compiler.CompileValue(member.TargetSchema),
                    materializeDefaults
                )
            );
        }
    }

    private sealed class JsonMemberWriter<TContainer, TValue>(
        IFunctionalMemberSchema<TContainer, TValue> member,
        IJsonValueWriter<TValue> valueWriter,
        bool materializeDefault
    ) : IJsonMemberWriter<TContainer>
    {
        public void Write(Utf8JsonWriter writer, TContainer container)
        {
            var value = member.GetValue(container);
            if (value is null && !member.IsRequired)
            {
                if (
                    !materializeDefault
                    || !TryCreateDefaultValue(
                        member.TargetSchema,
                        member.Traits,
                        out var defaultValue
                    )
                )
                {
                    return;
                }

                value = (TValue)defaultValue!;
            }

            writer.WritePropertyName(WireName(member.Traits, member.Name));
            valueWriter.Write(writer, value);
        }
    }

    private sealed class JsonUnionCaseWriterCompiler<TUnion>(JsonWriterCompiler compiler)
        : IFunctionalUnionCaseVisitor<TUnion>
    {
        private readonly List<IJsonUnionCaseWriter<TUnion>> writers = [];

        public IReadOnlyList<IJsonUnionCaseWriter<TUnion>> Writers => writers;

        public void Visit<TValue>(IFunctionalUnionCaseSchema<TUnion, TValue> @case)
        {
            writers.Add(
                new JsonUnionCaseWriter<TUnion, TValue>(
                    @case,
                    compiler.CompileValue(@case.TargetSchema)
                )
            );
        }
    }

    private sealed class JsonUnionCaseWriter<TUnion, TValue>(
        IFunctionalUnionCaseSchema<TUnion, TValue> @case,
        IJsonValueWriter<TValue> valueWriter
    ) : IJsonUnionCaseWriter<TUnion>
    {
        public bool TryWrite(Utf8JsonWriter writer, TUnion value)
        {
            if (!@case.Matches(value))
            {
                return false;
            }

            writer.WritePropertyName(WireName(@case.Traits, @case.Name));
            valueWriter.Write(writer, @case.GetValue(value));
            return true;
        }
    }

    private sealed class StructureJsonValueWriter<T>(
        IReadOnlyList<IJsonMemberWriter<T>> memberWriters
    ) : IJsonValueWriter<T>
    {
        public void Write(Utf8JsonWriter writer, T value)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartObject();
            foreach (var memberWriter in memberWriters)
            {
                memberWriter.Write(writer, value);
            }
            writer.WriteEndObject();
        }
    }

    private sealed class UnionJsonValueWriter<T>(IReadOnlyList<IJsonUnionCaseWriter<T>> caseWriters)
        : IJsonValueWriter<T>
    {
        public void Write(Utf8JsonWriter writer, T value)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartObject();
            foreach (var caseWriter in caseWriters)
            {
                if (caseWriter.TryWrite(writer, value))
                {
                    writer.WriteEndObject();
                    return;
                }
            }

            throw new InvalidOperationException($"No union case matched '{typeof(T).Name}'.");
        }
    }

    private sealed class ListJsonValueWriter<TCollection, TElement>(
        IFunctionalListSchema<TCollection, TElement> schema,
        IJsonValueWriter<TElement> elementWriter
    ) : IJsonValueWriter<TCollection>
    {
        public void Write(Utf8JsonWriter writer, TCollection value)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartArray();
            foreach (var element in schema.GetElements(value))
            {
                elementWriter.Write(writer, element);
            }
            writer.WriteEndArray();
        }
    }

    private sealed class MapJsonValueWriter<TDictionary, TValue>(
        IFunctionalMapSchema<TDictionary, TValue> schema,
        IJsonValueWriter<TValue> valueWriter
    ) : IJsonValueWriter<TDictionary>
    {
        public void Write(Utf8JsonWriter writer, TDictionary value)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartObject();
            foreach (var entry in schema.GetEntries(value))
            {
                writer.WritePropertyName(entry.Key);
                valueWriter.Write(writer, entry.Value);
            }
            writer.WriteEndObject();
        }
    }

    private sealed class NullableJsonValueWriter<T>(IJsonValueWriter<T> inner)
        : IJsonValueWriter<T?>
        where T : struct
    {
        public void Write(Utf8JsonWriter writer, T? value)
        {
            if (value.HasValue)
            {
                inner.Write(writer, value.Value);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }

    private sealed class BooleanJsonValueWriter : IJsonValueWriter<bool>
    {
        public void Write(Utf8JsonWriter writer, bool value) => writer.WriteBooleanValue(value);
    }

    private sealed class ByteJsonValueWriter : IJsonValueWriter<sbyte>
    {
        public void Write(Utf8JsonWriter writer, sbyte value) => writer.WriteNumberValue(value);
    }

    private sealed class ShortJsonValueWriter : IJsonValueWriter<short>
    {
        public void Write(Utf8JsonWriter writer, short value) => writer.WriteNumberValue(value);
    }

    private sealed class IntegerJsonValueWriter : IJsonValueWriter<int>
    {
        public void Write(Utf8JsonWriter writer, int value) => writer.WriteNumberValue(value);
    }

    private sealed class LongJsonValueWriter : IJsonValueWriter<long>
    {
        public void Write(Utf8JsonWriter writer, long value) => writer.WriteNumberValue(value);
    }

    private sealed class FloatJsonValueWriter : IJsonValueWriter<float>
    {
        public void Write(Utf8JsonWriter writer, float value) => WriteFloat(writer, value);
    }

    private sealed class DoubleJsonValueWriter : IJsonValueWriter<double>
    {
        public void Write(Utf8JsonWriter writer, double value) => WriteDouble(writer, value);
    }

    private sealed class BigIntegerJsonValueWriter : IJsonValueWriter<BigInteger>
    {
        public void Write(Utf8JsonWriter writer, BigInteger value) =>
            writer.WriteRawValue(value.ToString(CultureInfo.InvariantCulture), true);
    }

    private sealed class BigDecimalJsonValueWriter : IJsonValueWriter<decimal>
    {
        public void Write(Utf8JsonWriter writer, decimal value) => writer.WriteNumberValue(value);
    }

    private sealed class StringJsonValueWriter : IJsonValueWriter<string>
    {
        public void Write(Utf8JsonWriter writer, string value)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStringValue(value);
        }
    }

    private sealed class StringEnumJsonValueWriter<T> : IJsonValueWriter<T>
        where T : IFunctionalStringEnumValue<T>
    {
        public void Write(Utf8JsonWriter writer, T value) => writer.WriteStringValue(value.Value);
    }

    private sealed class IntEnumJsonValueWriter<T>(FunctionalIntEnumSchema<T> schema)
        : IJsonValueWriter<T>
        where T : struct, Enum
    {
        public void Write(Utf8JsonWriter writer, T value) =>
            writer.WriteNumberValue(schema.GetIntegerValue(value));
    }

    private sealed class BlobJsonValueWriter : IJsonValueWriter<byte[]>
    {
        public void Write(Utf8JsonWriter writer, byte[] value)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteBase64StringValue(value);
        }
    }

    private sealed class TimestampJsonValueWriter(string format) : IJsonValueWriter<DateTimeOffset>
    {
        public void Write(Utf8JsonWriter writer, DateTimeOffset value) =>
            TimestampFormat.Write(writer, value, format);
    }

    /// <summary>
    /// Smithy timestamp wire formats for JSON bodies. The body default is <c>epoch-seconds</c>;
    /// <c>@timestampFormat</c> on the member or target shape overrides it.
    /// </summary>
    private static class TimestampFormat
    {
        private static readonly ShapeId TimestampFormatTrait = new("smithy.api", "timestampFormat");

        public static string Resolve(
            IReadOnlyDictionary<ShapeId, Trait>? memberTraits,
            FunctionalSchema schema
        )
        {
            if (
                memberTraits is not null
                && memberTraits.TryGetValue(TimestampFormatTrait, out var memberTrait)
            )
            {
                return memberTrait.Value.AsString();
            }

            if (schema.Resolved.Traits.TryGetValue(TimestampFormatTrait, out var schemaTrait))
            {
                return schemaTrait.Value.AsString();
            }

            return "epoch-seconds";
        }

        public static void Write(Utf8JsonWriter writer, DateTimeOffset value, string format)
        {
            switch (format)
            {
                case "epoch-seconds":
                    var utcTicks = value.ToUniversalTime().Ticks;
                    if (utcTicks % TimeSpan.TicksPerSecond == 0)
                    {
                        writer.WriteNumberValue(value.ToUnixTimeSeconds());
                    }
                    else
                    {
                        writer.WriteNumberValue(value.ToUnixTimeMilliseconds() / 1000.0m);
                    }

                    break;
                case "http-date":
                    writer.WriteStringValue(
                        value.ToUniversalTime().ToString("r", CultureInfo.InvariantCulture)
                    );
                    break;
                default: // date-time (RFC3339)
                    var utc = value.ToUniversalTime();
                    var text =
                        utc.Ticks % TimeSpan.TicksPerSecond == 0
                            ? utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
                            : utc.ToString(
                                "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
                                CultureInfo.InvariantCulture
                            );
                    writer.WriteStringValue(text);
                    break;
            }
        }

        public static DateTimeOffset Read(JsonElement value, string format)
        {
            return format switch
            {
                "epoch-seconds" => DateTimeOffset.FromUnixTimeMilliseconds(
                    (long)(value.GetDouble() * 1000)
                ),
                "http-date" => DateTimeOffset.ParseExact(
                    value.GetString()!,
                    "r",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None
                ),
                _ => DateTimeOffset.Parse(
                    value.GetString()!,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind
                ),
            };
        }
    }

    private sealed class DocumentJsonValueWriter : IJsonValueWriter<Document>
    {
        public void Write(Utf8JsonWriter writer, Document value) =>
            DocumentJsonWriter.Write(writer, value);
    }

    private static void WriteValue(Utf8JsonWriter writer, FunctionalSchema schema, object? value)
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
                writer.WriteStringValue(((IFunctionalStringEnumValue)value).Value);
                break;
            case ShapeKind.IntEnum:
                writer.WriteNumberValue(
                    ((IFunctionalIntEnumSchema)schema).GetIntegerValueObject(value)
                );
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

    private static void WriteValue<T>(Utf8JsonWriter writer, FunctionalSchema<T> schema, T value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        if (schema.Resolved is IFunctionalStructSchema<T> structSchema)
        {
            WriteStructure(writer, structSchema, value);
            return;
        }

        WriteValue(writer, (FunctionalSchema)schema, value);
    }

    private static void WriteStructure<T>(
        Utf8JsonWriter writer,
        IFunctionalStructSchema<T> schema,
        T value
    )
    {
        writer.WriteStartObject();
        schema.VisitMembers(new JsonWriteMemberVisitor<T>(writer, value));
        writer.WriteEndObject();
    }

    private static void WriteProjection<T>(
        Utf8JsonWriter writer,
        FunctionalStructProjection<T> projection,
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
    ) : IFunctionalMemberVisitor<TContainer>
    {
        public void Visit<TValue>(IFunctionalMemberSchema<TContainer, TValue> member)
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

    private static void WriteUnion(
        Utf8JsonWriter writer,
        IFunctionalUnionSchema schema,
        object value
    )
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
        IFunctionalUnionSchema schema,
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
            ShapeKind.Enum => ((IFunctionalStringEnumSchema)schema).CreateObject(
                value.GetString()!
            ),
            ShapeKind.IntEnum => ((IFunctionalIntEnumSchema)schema).CreateObject(value.GetInt32()),
            ShapeKind.Blob => value.GetBytesFromBase64(),
            ShapeKind.Timestamp => TimestampFormat.Read(
                value,
                TimestampFormat.Resolve(null, schema)
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

    private static FunctionalSchema UnwrapNullable(FunctionalSchema schema)
    {
        var resolved = schema.Resolved;
        return resolved is IFunctionalNullableSchema nullable ? nullable.Target.Resolved : resolved;
    }

    private static readonly ShapeId ClientOptionalTrait = new("smithy.api", "clientOptional");
    private static readonly ShapeId DefaultTrait = new("smithy.api", "default");
    private static readonly ShapeId JsonNameTrait = new("smithy.api", "jsonName");
    private static readonly ShapeId AlloyDiscriminatedTrait = new("alloy", "discriminated");
    private static readonly ShapeId AlloyJsonUnknownTrait = new("alloy", "jsonUnknown");

    // The JSON property name for a member or union case: @jsonName if present, else the name.
    private static string WireName(IReadOnlyDictionary<ShapeId, Trait> traits, string fallback) =>
        traits.TryGetValue(JsonNameTrait, out var trait) ? trait.Value.AsString() : fallback;

    private static bool IsOpenUnion(IFunctionalUnionSchema schema) =>
        ((FunctionalSchema)schema).Traits.ContainsKey(AlloyDiscriminatedTrait)
        || GetJsonUnknownCase(schema) is not null;

    private static bool TryCreateDefaultValue(
        FunctionalSchema schema,
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

    private static object? CreateDefaultValue(FunctionalSchema schema, Document value)
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
            ShapeKind.Enum => ((IFunctionalStringEnumSchema)schema).CreateObject(value.AsString()),
            ShapeKind.IntEnum => ((IFunctionalIntEnumSchema)schema).CreateObject(
                (int)value.AsNumber()
            ),
            ShapeKind.Blob => Convert.FromBase64String(value.AsString()),
            ShapeKind.Timestamp => DateTimeOffset.FromUnixTimeSeconds((long)value.AsNumber()),
            ShapeKind.Document => value,
            ShapeKind.List or ShapeKind.Set when schema.Resolved is IFunctionalListSchema list =>
                CreateDefaultList(list, value),
            ShapeKind.Map when schema.Resolved is IFunctionalMapSchema map => CreateDefaultMap(
                map,
                value
            ),
            _ => null,
        };
    }

    private static object CreateDefaultList(IFunctionalListSchema schema, Document value)
    {
        var builder = schema.CreateBuilder();
        foreach (var item in value.AsArray())
        {
            schema.AddObject(builder, CreateDefaultValue(UnwrapNullable(schema.Element), item));
        }

        return schema.BuildObject(builder);
    }

    private static object CreateDefaultMap(IFunctionalMapSchema schema, Document value)
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

    private static bool TryGetDiscriminatorName(
        IFunctionalUnionSchema schema,
        out string discriminatorName
    )
    {
        if (((FunctionalSchema)schema).Traits.TryGetValue(AlloyDiscriminatedTrait, out var trait))
        {
            discriminatorName = trait.Value.AsString();
            return true;
        }

        discriminatorName = string.Empty;
        return false;
    }

    private static IFunctionalUnionCaseSchema? GetJsonUnknownCase(IFunctionalUnionSchema schema) =>
        schema.Cases.FirstOrDefault(IsJsonUnknownCase);

    private static bool IsJsonUnknownCase(IFunctionalUnionCaseSchema @case) =>
        @case.Traits.ContainsKey(AlloyJsonUnknownTrait);

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
        FunctionalStructProjection<T> projection,
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

    private static object ReadUnion(IFunctionalUnionSchema schema, JsonElement value)
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
        IFunctionalUnionSchema schema,
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
                $"Discriminated union '{((FunctionalSchema)schema).Id}' is missing an unknown JSON case."
            );
        return unknownCase.CreateObject(Document.FromJsonElement(value));
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
