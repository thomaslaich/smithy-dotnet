using System.Globalization;
using System.Numerics;
using System.Text.Json;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Json;

public static partial class JsonCodec
{
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
        private readonly Dictionary<Schema, object> cache = new(ReferenceEqualityComparer.Instance);

        public static IJsonValueWriter<T> Compile<T>(Schema<T> schema)
        {
            ArgumentNullException.ThrowIfNull(schema);
            return new JsonWriterCompiler().CompileValue(schema);
        }

        public static StructureJsonValueWriter<T> Compile<T>(
            StructProjection<T> projection,
            bool materializeTopLevelDefaults = true
        )
        {
            ArgumentNullException.ThrowIfNull(projection);
            var compiler = new JsonWriterCompiler();
            return compiler.CompileProjection(projection, materializeTopLevelDefaults);
        }

        public IJsonValueWriter<T> CompileValue<T>(Schema<T> schema)
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

        private IJsonValueWriter<T> CompileValueCore<T>(Schema<T> schema, Schema resolved)
        {
            if (resolved is INullableSchema)
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
                ShapeKind.Structure when resolved is IStructSchema<T> structSchema =>
                    CompileStructure(structSchema),
                ShapeKind.Union when resolved is IUnionSchema<T> unionSchema => CompileUnion(
                    unionSchema
                ),
                ShapeKind.List or ShapeKind.Set => (IJsonValueWriter<T>)
                    CompileList((dynamic)resolved),
                ShapeKind.Map => (IJsonValueWriter<T>)CompileMap((dynamic)resolved),
                _ => throw new NotSupportedException(
                    $"JSON codec does not support schema kind '{schema.Kind}'."
                ),
            };
        }

        private NullableJsonValueWriter<T> CompileNullable<T>(NullableSchema<T> schema)
            where T : struct => new NullableJsonValueWriter<T>(CompileValue(schema.TargetSchema));

        private static StringEnumJsonValueWriter<T> CompileStringEnum<T>(StringEnumSchema<T> schema)
            where T : IStringEnumValue<T> => new StringEnumJsonValueWriter<T>();

        private static IntEnumJsonValueWriter<T> CompileIntEnum<T>(IntEnumSchema<T> schema)
            where T : struct, Enum => new IntEnumJsonValueWriter<T>(schema);

        private StructureJsonValueWriter<T> CompileStructure<T>(IStructSchema<T> schema)
        {
            var visitor = new JsonMemberWriterCompiler<T>(this, materializeDefaults: true);
            schema.VisitMembers(visitor);
            return new StructureJsonValueWriter<T>(visitor.Writers);
        }

        private StructureJsonValueWriter<T> CompileProjection<T>(
            StructProjection<T> projection,
            bool materializeTopLevelDefaults
        )
        {
            var visitor = new JsonMemberWriterCompiler<T>(this, materializeTopLevelDefaults);
            projection.VisitMembers(visitor);
            return new StructureJsonValueWriter<T>(visitor.Writers);
        }

        private ListJsonValueWriter<TCollection, TElement> CompileList<TCollection, TElement>(
            IListSchema<TCollection, TElement> schema
        ) =>
            new ListJsonValueWriter<TCollection, TElement>(
                schema,
                CompileValue(schema.ElementSchema)
            );

        private MapJsonValueWriter<TDictionary, TValue> CompileMap<TDictionary, TValue>(
            IMapSchema<TDictionary, TValue> schema
        ) => new MapJsonValueWriter<TDictionary, TValue>(schema, CompileValue(schema.ValueSchema));

        private IJsonValueWriter<T> CompileUnion<T>(IUnionSchema<T> schema)
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
    ) : IMemberVisitor<TContainer>
    {
        private readonly List<IJsonMemberWriter<TContainer>> writers = [];

        public IReadOnlyList<IJsonMemberWriter<TContainer>> Writers => writers;

        public void Visit<TValue>(IMemberSchema<TContainer, TValue> member)
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
        IMemberSchema<TContainer, TValue> member,
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
        : IUnionCaseVisitor<TUnion>
    {
        private readonly List<IJsonUnionCaseWriter<TUnion>> writers = [];

        public IReadOnlyList<IJsonUnionCaseWriter<TUnion>> Writers => writers;

        public void Visit<TValue>(IUnionCaseSchema<TUnion, TValue> @case)
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
        IUnionCaseSchema<TUnion, TValue> @case,
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
        IListSchema<TCollection, TElement> schema,
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
        IMapSchema<TDictionary, TValue> schema,
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
        where T : IStringEnumValue<T>
    {
        public void Write(Utf8JsonWriter writer, T value) => writer.WriteStringValue(value.Value);
    }

    private sealed class IntEnumJsonValueWriter<T>(IntEnumSchema<T> schema) : IJsonValueWriter<T>
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

    private sealed class DocumentJsonValueWriter : IJsonValueWriter<Document>
    {
        public void Write(Utf8JsonWriter writer, Document value) =>
            DocumentJsonWriter.Write(writer, value);
    }
}
