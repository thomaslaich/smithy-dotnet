using System.Formats.Cbor;
using System.Globalization;
using System.Numerics;
using NSmithy.Core;
using NSmithy.Core.Serde;
using static NSmithy.Codecs.Cbor.CborWire;

namespace NSmithy.Codecs.Cbor;

internal sealed class CborReaderCompiler : ISchemaVisitor<object>
{
    private readonly Dictionary<Schema, object> cache = new(ReferenceEqualityComparer.Instance);

    public static ICborValueReader<T> Compile<T>(Schema<T> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new CborReaderCompiler().CompileValue(schema);
    }

    public ICborValueReader<T> CompileValue<T>(Schema<T> schema)
    {
        var resolved = schema.Resolved;
        if (cache.TryGetValue(resolved, out var cached))
        {
            return (ICborValueReader<T>)cached;
        }

        var deferred = new DeferredCborValueReader<T>();
        cache.Add(resolved, deferred);
        deferred.Set((ICborValueReader<T>)resolved.Accept(this));
        return deferred;
    }

    public ICborValueReader<T> CompileValue<T>(
        Schema<T> schema,
        IReadOnlyDictionary<ShapeId, Trait> memberTraits
    )
    {
        ArgumentNullException.ThrowIfNull(memberTraits);
        return CompileValue(schema);
    }

    public object VisitBoolean(Schema<bool> schema) =>
        new DelegatingCborValueReader<bool>(static reader => reader.ReadBoolean());

    public object VisitByte(Schema<sbyte> schema) =>
        new DelegatingCborValueReader<sbyte>(static reader =>
            Convert.ToSByte(ReadInteger(reader), CultureInfo.InvariantCulture)
        );

    public object VisitShort(Schema<short> schema) =>
        new DelegatingCborValueReader<short>(static reader =>
            Convert.ToInt16(ReadInteger(reader), CultureInfo.InvariantCulture)
        );

    public object VisitInteger(Schema<int> schema) =>
        new DelegatingCborValueReader<int>(static reader =>
            Convert.ToInt32(ReadInteger(reader), CultureInfo.InvariantCulture)
        );

    public object VisitLong(Schema<long> schema) =>
        new DelegatingCborValueReader<long>(static reader =>
            Convert.ToInt64(ReadInteger(reader), CultureInfo.InvariantCulture)
        );

    public object VisitFloat(Schema<float> schema) =>
        new DelegatingCborValueReader<float>(static reader =>
            reader.PeekState() == CborReaderState.SinglePrecisionFloat
                ? reader.ReadSingle()
                : Convert.ToSingle(reader.ReadDouble(), CultureInfo.InvariantCulture)
        );

    public object VisitDouble(Schema<double> schema) =>
        new DelegatingCborValueReader<double>(static reader =>
            reader.PeekState() switch
            {
                CborReaderState.SinglePrecisionFloat => reader.ReadSingle(),
                CborReaderState.HalfPrecisionFloat => (double)reader.ReadHalf(),
                _ => reader.ReadDouble(),
            }
        );

    public object VisitBigInteger(Schema<BigInteger> schema) =>
        new DelegatingCborValueReader<BigInteger>(ReadBigInteger);

    public object VisitBigDecimal(Schema<decimal> schema) =>
        new DelegatingCborValueReader<decimal>(ReadBigDecimal);

    public object VisitString(Schema<string> schema) =>
        new DelegatingCborValueReader<string>(ReadNullableTextString);

    public object VisitBlob(Schema<byte[]> schema) =>
        new DelegatingCborValueReader<byte[]>(ReadNullableByteString);

    public object VisitTimestamp(Schema<DateTimeOffset> schema) =>
        new DelegatingCborValueReader<DateTimeOffset>(ReadTimestamp);

    public object VisitDocument(Schema<Document> schema) =>
        throw new NotSupportedException("Smithy Document values are not supported by rpcv2Cbor.");

    public object VisitNullable<T>(NullableSchema<T> schema)
        where T : struct => new NullableCborValueReader<T>(CompileValue(schema.TargetSchema));

    public object VisitEventStream<TEvent>(EventStreamSchema<TEvent> schema) =>
        throw new NotSupportedException("CBOR codec does not support event stream schemas.");

    public object VisitList<TCollection, TElement, TBuilder>(
        IListSchema<TCollection, TElement, TBuilder> schema
    ) =>
        new ListCborValueReader<TCollection, TElement, TBuilder>(
            schema,
            CompileValue(schema.ElementSchema)
        );

    public object VisitMap<TDictionary, TValue, TBuilder>(
        IMapSchema<TDictionary, TValue, TBuilder> schema
    ) =>
        new MapCborValueReader<TDictionary, TValue, TBuilder>(
            schema,
            CompileValue(schema.ValueSchema)
        );

    public object VisitStruct<T, TBuilder>(IStructSchema<T, TBuilder> schema)
    {
        var visitor = new CborMemberReaderCompiler<T, TBuilder>(this);
        schema.VisitMembers(visitor);
        return new StructureCborValueReader<T, TBuilder>(
            schema.CreateTypedBuilder,
            schema.Build,
            visitor.Readers
        );
    }

    public object VisitUnion<T>(IUnionSchema<T> schema)
    {
        var visitor = new CborUnionCaseReaderCompiler<T>(this);
        schema.VisitCases(visitor);
        return new UnionCborValueReader<T>(visitor.Readers);
    }

    public object VisitStringEnum<T>(StringEnumSchema<T> schema)
        where T : IStringEnumValue<T> => new StringEnumCborValueReader<T>(schema);

    public object VisitIntEnum<T>(IntEnumSchema<T> schema)
        where T : struct, Enum => new IntEnumCborValueReader<T>(schema);
}

internal sealed class DeferredCborValueReader<T> : ICborValueReader<T>
{
    private ICborValueReader<T>? inner;

    public void Set(ICborValueReader<T> reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        inner = reader;
    }

    public T Read(CborReader reader)
    {
        if (inner is null)
        {
            throw new InvalidOperationException("CBOR reader has not been initialized.");
        }

        return inner.Read(reader);
    }
}

internal sealed class DelegatingCborValueReader<T>(Func<CborReader, T> read) : ICborValueReader<T>
{
    public T Read(CborReader reader) => read(reader);
}

internal sealed class NullableCborValueReader<T>(ICborValueReader<T> inner) : ICborValueReader<T?>
    where T : struct
{
    public T? Read(CborReader reader)
    {
        if (reader.PeekState() != CborReaderState.Null)
        {
            return inner.Read(reader);
        }

        reader.ReadNull();
        return null;
    }
}

internal sealed class StringEnumCborValueReader<T>(StringEnumSchema<T> schema) : ICborValueReader<T>
    where T : IStringEnumValue<T>
{
    public T Read(CborReader reader) => schema.Create(reader.ReadTextString());
}

internal sealed class IntEnumCborValueReader<T>(IntEnumSchema<T> schema) : ICborValueReader<T>
    where T : struct, Enum
{
    public T Read(CborReader reader) =>
        schema.Create(Convert.ToInt32(ReadInteger(reader), CultureInfo.InvariantCulture));
}

internal sealed class CborMemberReaderCompiler<TContainer, TBuilder>(CborReaderCompiler compiler)
    : IMemberVisitor<TContainer, TBuilder>
{
    private readonly List<ICborMemberReader<TBuilder>> readers = [];

    public IReadOnlyList<ICborMemberReader<TBuilder>> Readers => readers;

    public void Visit<TValue>(IMemberSchema<TContainer, TBuilder, TValue> member)
    {
        readers.Add(
            new CborMemberReader<TContainer, TBuilder, TValue>(
                member,
                compiler.CompileValue(member.TargetSchema, member.MemberTraits)
            )
        );
    }
}

internal sealed class CborMemberReader<TContainer, TBuilder, TValue>(
    IMemberSchema<TContainer, TBuilder, TValue> member,
    ICborValueReader<TValue> valueReader
) : ICborMemberReader<TBuilder>
{
    public string Name => member.Name;

    public bool IsRequired => member.IsRequired;

    public void ReadMissing(TBuilder builder)
    {
        if (
            TryCreateDefaultValue(
                member.TargetSchema,
                member.MemberTraits,
                out TValue? defaultValue
            )
        )
        {
            member.SetValue(builder, defaultValue!);
        }
    }

    public void ReadInto(TBuilder builder, CborReader reader) =>
        member.SetValue(builder, valueReader.Read(reader));
}

internal sealed class StructureCborValueReader<T, TBuilder>(
    Func<TBuilder> createBuilder,
    Func<TBuilder, T> build,
    IReadOnlyList<ICborMemberReader<TBuilder>> memberReaders
) : ICborValueReader<T>
{
    private readonly Dictionary<string, ICborMemberReader<TBuilder>> readersByName =
        memberReaders.ToDictionary(reader => reader.Name, StringComparer.Ordinal);

    public T Read(CborReader reader)
    {
        if (reader.PeekState() == CborReaderState.Null)
        {
            reader.ReadNull();
            return default!;
        }

        if (reader.PeekState() != CborReaderState.StartMap)
        {
            throw new InvalidOperationException("Expected CBOR map for structure.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var builder = createBuilder();
        reader.ReadStartMap();
        while (reader.PeekState() != CborReaderState.EndMap)
        {
            var name = reader.ReadTextString();
            if (readersByName.TryGetValue(name, out var memberReader))
            {
                if (reader.PeekState() == CborReaderState.Null && memberReader.IsRequired)
                {
                    reader.ReadNull();
                    throw new InvalidOperationException(
                        $"Required member '{name}' cannot be null."
                    );
                }

                seen.Add(name);
                memberReader.ReadInto(builder, reader);
            }
            else
            {
                reader.SkipValue();
            }
        }

        reader.ReadEndMap();
        foreach (var memberReader in memberReaders)
        {
            if (seen.Contains(memberReader.Name))
            {
                continue;
            }

            if (memberReader.IsRequired)
            {
                throw new InvalidOperationException(
                    $"Missing required member '{memberReader.Name}'."
                );
            }

            memberReader.ReadMissing(builder);
        }

        return build(builder);
    }
}

internal sealed class CborUnionCaseReaderCompiler<TUnion>(CborReaderCompiler compiler)
    : IUnionCaseVisitor<TUnion>
{
    private readonly List<ICborUnionCaseReader<TUnion>> readers = [];

    public IReadOnlyList<ICborUnionCaseReader<TUnion>> Readers => readers;

    public void Visit<TValue>(IUnionCaseSchema<TUnion, TValue> unionCase)
    {
        readers.Add(
            new CborUnionCaseReader<TUnion, TValue>(
                unionCase,
                compiler.CompileValue(unionCase.TargetSchema)
            )
        );
    }
}

internal sealed class CborUnionCaseReader<TUnion, TValue>(
    IUnionCaseSchema<TUnion, TValue> unionCase,
    ICborValueReader<TValue> valueReader
) : ICborUnionCaseReader<TUnion>
{
    public string Name => unionCase.Name;

    public TUnion Read(CborReader reader) => unionCase.Create(valueReader.Read(reader));
}

internal sealed class UnionCborValueReader<T>(IReadOnlyList<ICborUnionCaseReader<T>> caseReaders)
    : ICborValueReader<T>
{
    private readonly Dictionary<string, ICborUnionCaseReader<T>> readersByName =
        caseReaders.ToDictionary(reader => reader.Name, StringComparer.Ordinal);

    public T Read(CborReader reader)
    {
        if (reader.PeekState() != CborReaderState.StartMap)
        {
            throw new InvalidOperationException("Expected single-entry CBOR map for union.");
        }

        reader.ReadStartMap();
        if (reader.PeekState() == CborReaderState.EndMap)
        {
            throw new InvalidOperationException("Expected single-entry CBOR map for union.");
        }

        var name = reader.ReadTextString();
        if (!readersByName.TryGetValue(name, out var caseReader))
        {
            throw new InvalidOperationException($"Unknown union member '{name}'.");
        }

        var value = caseReader.Read(reader);
        while (reader.PeekState() != CborReaderState.EndMap)
        {
            reader.SkipValue();
        }

        reader.ReadEndMap();
        return value;
    }
}

internal sealed class ListCborValueReader<TCollection, TElement, TBuilder>(
    IListSchema<TCollection, TElement, TBuilder> schema,
    ICborValueReader<TElement> elementReader
) : ICborValueReader<TCollection>
{
    public TCollection Read(CborReader reader)
    {
        if (reader.PeekState() != CborReaderState.StartArray)
        {
            throw new InvalidOperationException("Expected CBOR array for list.");
        }

        var builder = schema.CreateTypedBuilder();
        reader.ReadStartArray();
        while (reader.PeekState() != CborReaderState.EndArray)
        {
            schema.Add(builder, elementReader.Read(reader));
        }

        reader.ReadEndArray();
        return schema.Build(builder);
    }
}

internal sealed class MapCborValueReader<TDictionary, TValue, TBuilder>(
    IMapSchema<TDictionary, TValue, TBuilder> schema,
    ICborValueReader<TValue> valueReader
) : ICborValueReader<TDictionary>
{
    public TDictionary Read(CborReader reader)
    {
        if (reader.PeekState() != CborReaderState.StartMap)
        {
            throw new InvalidOperationException("Expected CBOR map for map shape.");
        }

        var builder = schema.CreateTypedBuilder();
        reader.ReadStartMap();
        while (reader.PeekState() != CborReaderState.EndMap)
        {
            schema.Add(builder, reader.ReadTextString(), valueReader.Read(reader));
        }

        reader.ReadEndMap();
        return schema.Build(builder);
    }
}
