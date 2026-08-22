using System.Globalization;
using System.Numerics;
using System.Text;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Proto;

internal interface IProtoMessageReader<out T>
{
    T Read(ReadOnlySpan<byte> bytes);
}

internal interface IProtoValueReader<T>
{
    T ReadBody(ref ProtoReader reader, WireType wireType);
}

internal interface IProtoFieldReader<in TBuilder>
{
    int FieldNumber { get; }

    void ReadInto(
        TBuilder builder,
        ref ProtoReader reader,
        WireType wireType,
        ref ProtoReadState state
    );
}

internal interface IProtoFieldCompleter<in TBuilder>
{
    void Complete(TBuilder builder, ref ProtoReadState state);
}

internal interface IProtoUnionCaseReader<out TUnion>
{
    int FieldNumber { get; }

    TUnion Read(ref ProtoReader reader, WireType wireType);
}

internal struct ProtoReadState(int slotCount)
{
    private object? firstBuilder;
    private object?[]? additionalBuilders;

    public TBuilder GetOrCreate<TBuilder>(int slot, Func<TBuilder> create)
    {
        if (slot == 0)
        {
            if (firstBuilder is TBuilder first)
            {
                return first;
            }

            var created = create();
            firstBuilder = created;
            return created;
        }

        additionalBuilders ??= new object?[slotCount - 1];
        if (additionalBuilders[slot - 1] is TBuilder builder)
        {
            return builder;
        }

        var additional = create();
        additionalBuilders[slot - 1] = additional;
        return additional;
    }

    public bool TryGet<TBuilder>(int slot, out TBuilder builder)
    {
        var value = slot == 0 ? firstBuilder : additionalBuilders?[slot - 1];
        if (value is TBuilder typed)
        {
            builder = typed;
            return true;
        }

        builder = default!;
        return false;
    }
}

internal static class ProtoReaderCompiler
{
    public static IProtoMessageReader<T> Compile<T>(Schema<T> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new ProtoValueReaderCompiler().CompileMessage(schema);
    }
}

internal sealed class ProtoValueReaderCompiler : ISchemaVisitor<object>
{
    private static readonly IReadOnlyDictionary<ShapeId, Trait> EmptyTraits =
        new Dictionary<ShapeId, Trait>();

    private readonly SchemaCompilationCache cache = new();

    public IProtoMessageReader<T> CompileMessage<T>(Schema<T> schema)
    {
        var unwrapped = ProtoWire.Unwrap(schema);
        return (IProtoMessageReader<T>)unwrapped.Accept(new MessageReaderVisitor(this));
    }

    public IProtoValueReader<T> CompileValue<T>(Schema<T> schema) =>
        CompileValue(schema, EmptyTraits);

    public IProtoValueReader<T> CompileValue<T>(
        Schema<T> schema,
        IReadOnlyDictionary<ShapeId, Trait> traits
    )
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(traits);

        var resolved = schema.Resolved;
        if (traits.Count != 0)
        {
            return (IProtoValueReader<T>)
                resolved.Accept(new MemberTraitProtoReaderCompiler(this, traits));
        }

        return cache.GetOrCompile<IProtoValueReader<T>, DeferredProtoValueReader<T>>(
            resolved,
            static () => new DeferredProtoValueReader<T>(),
            target => (IProtoValueReader<T>)target.Accept(this)
        );
    }

    public object VisitBoolean(Schema<bool> schema) => Scalar(schema, EmptyTraits);

    public object VisitByte(Schema<sbyte> schema) => Scalar(schema, EmptyTraits);

    public object VisitShort(Schema<short> schema) => Scalar(schema, EmptyTraits);

    public object VisitInteger(Schema<int> schema) => Scalar(schema, EmptyTraits);

    public object VisitLong(Schema<long> schema) => Scalar(schema, EmptyTraits);

    public object VisitFloat(Schema<float> schema) => Scalar(schema, EmptyTraits);

    public object VisitDouble(Schema<double> schema) => Scalar(schema, EmptyTraits);

    public object VisitBigInteger(Schema<BigInteger> schema) => Scalar(schema, EmptyTraits);

    public object VisitBigDecimal(Schema<decimal> schema) => Scalar(schema, EmptyTraits);

    public object VisitString(Schema<string> schema) => Scalar(schema, EmptyTraits);

    public object VisitBlob(Schema<byte[]> schema) => Scalar(schema, EmptyTraits);

    public object VisitTimestamp(Schema<DateTimeOffset> schema) => Scalar(schema, EmptyTraits);

    public object VisitDocument(Schema<Document> schema) => new DocumentProtoValueReader();

    public object VisitNullable<T>(NullableSchema<T> schema)
        where T : struct => new NullableProtoValueReader<T>(CompileValue(schema.TargetSchema));

    public object VisitStreamingBlob(Schema<Stream> schema) =>
        throw new NotSupportedException("Proto codec does not support streaming blob schemas.");

    public object VisitEventStream<TEvent>(EventStreamSchema<TEvent> schema) =>
        throw new NotSupportedException("Proto codec does not support event stream schemas.");

    public object VisitList<TCollection, TElement, TBuilder>(
        IListSchema<TCollection, TElement, TBuilder> schema
    ) => UnsupportedAggregateValue();

    public object VisitMap<TDictionary, TValue, TBuilder>(
        IMapSchema<TDictionary, TValue, TBuilder> schema
    ) => UnsupportedAggregateValue();

    public object VisitStruct<T, TBuilder>(IStructSchema<T, TBuilder> schema)
    {
        var visitor = new ProtoFieldReaderCompiler<T, TBuilder>(this);
        schema.VisitMembers(visitor);
        return new MessageProtoValueReader<T>(
            new StructureProtoMessageReader<T, TBuilder>(
                schema.CreateTypedBuilder,
                schema.Build,
                visitor.Readers,
                visitor.StateSlotCount
            )
        );
    }

    public object VisitUnion<T>(IUnionSchema<T> schema)
    {
        var visitor = new ProtoUnionCaseReaderCompiler<T>(this);
        schema.VisitCases(visitor);
        return new MessageProtoValueReader<T>(new UnionProtoMessageReader<T>(visitor.Readers));
    }

    public object VisitStringEnum<T>(StringEnumSchema<T> schema)
        where T : IStringEnumValue<T> => Scalar(schema, EmptyTraits);

    public object VisitIntEnum<T>(IntEnumSchema<T> schema)
        where T : struct, Enum => Scalar(schema, EmptyTraits);

    private static ScalarProtoValueReader<T> Scalar<T>(
        Schema<T> schema,
        IReadOnlyDictionary<ShapeId, Trait> traits
    ) => new(schema, traits);

    private static object UnsupportedAggregateValue() =>
        throw new NotSupportedException(
            "Proto codec cannot decode list or map schemas as standalone protobuf values."
        );
}

internal sealed class MessageReaderVisitor(ProtoValueReaderCompiler compiler)
    : ISchemaVisitor<object>
{
    public object VisitBoolean(Schema<bool> schema) => Unsupported();

    public object VisitByte(Schema<sbyte> schema) => Unsupported();

    public object VisitShort(Schema<short> schema) => Unsupported();

    public object VisitInteger(Schema<int> schema) => Unsupported();

    public object VisitLong(Schema<long> schema) => Unsupported();

    public object VisitFloat(Schema<float> schema) => Unsupported();

    public object VisitDouble(Schema<double> schema) => Unsupported();

    public object VisitBigInteger(Schema<BigInteger> schema) => Unsupported();

    public object VisitBigDecimal(Schema<decimal> schema) => Unsupported();

    public object VisitString(Schema<string> schema) => Unsupported();

    public object VisitBlob(Schema<byte[]> schema) => Unsupported();

    public object VisitTimestamp(Schema<DateTimeOffset> schema) => Unsupported();

    public object VisitDocument(Schema<Document> schema) => Unsupported();

    public object VisitNullable<T>(NullableSchema<T> schema)
        where T : struct => Unsupported();

    public object VisitStreamingBlob(Schema<Stream> schema) => Unsupported();

    public object VisitEventStream<TEvent>(EventStreamSchema<TEvent> schema) => Unsupported();

    public object VisitList<TCollection, TElement, TBuilder>(
        IListSchema<TCollection, TElement, TBuilder> schema
    ) => Unsupported();

    public object VisitMap<TDictionary, TValue, TBuilder>(
        IMapSchema<TDictionary, TValue, TBuilder> schema
    ) => Unsupported();

    public object VisitStruct<T, TBuilder>(IStructSchema<T, TBuilder> schema)
    {
        var visitor = new ProtoFieldReaderCompiler<T, TBuilder>(compiler);
        schema.VisitMembers(visitor);
        return new StructureProtoMessageReader<T, TBuilder>(
            schema.CreateTypedBuilder,
            schema.Build,
            visitor.Readers,
            visitor.StateSlotCount
        );
    }

    public object VisitUnion<T>(IUnionSchema<T> schema)
    {
        var visitor = new ProtoUnionCaseReaderCompiler<T>(compiler);
        schema.VisitCases(visitor);
        return new UnionProtoMessageReader<T>(visitor.Readers);
    }

    public object VisitStringEnum<T>(StringEnumSchema<T> schema)
        where T : IStringEnumValue<T> => Unsupported();

    public object VisitIntEnum<T>(IntEnumSchema<T> schema)
        where T : struct, Enum => Unsupported();

    private static object Unsupported() =>
        throw new InvalidOperationException(
            "Protobuf messages must be backed by a structure or union schema."
        );
}

internal sealed class MemberTraitProtoReaderCompiler(
    ProtoValueReaderCompiler inner,
    IReadOnlyDictionary<ShapeId, Trait> traits
) : ISchemaVisitor<object>
{
    public object VisitBoolean(Schema<bool> schema) =>
        new ScalarProtoValueReader<bool>(schema, traits);

    public object VisitByte(Schema<sbyte> schema) =>
        new ScalarProtoValueReader<sbyte>(schema, traits);

    public object VisitShort(Schema<short> schema) =>
        new ScalarProtoValueReader<short>(schema, traits);

    public object VisitInteger(Schema<int> schema) =>
        new ScalarProtoValueReader<int>(schema, traits);

    public object VisitLong(Schema<long> schema) =>
        new ScalarProtoValueReader<long>(schema, traits);

    public object VisitFloat(Schema<float> schema) =>
        new ScalarProtoValueReader<float>(schema, traits);

    public object VisitDouble(Schema<double> schema) =>
        new ScalarProtoValueReader<double>(schema, traits);

    public object VisitBigInteger(Schema<BigInteger> schema) =>
        new ScalarProtoValueReader<BigInteger>(schema, traits);

    public object VisitBigDecimal(Schema<decimal> schema) =>
        new ScalarProtoValueReader<decimal>(schema, traits);

    public object VisitString(Schema<string> schema) =>
        new ScalarProtoValueReader<string>(schema, traits);

    public object VisitBlob(Schema<byte[]> schema) =>
        new ScalarProtoValueReader<byte[]>(schema, traits);

    public object VisitTimestamp(Schema<DateTimeOffset> schema) =>
        new ScalarProtoValueReader<DateTimeOffset>(schema, traits);

    public object VisitDocument(Schema<Document> schema) => inner.CompileValue(schema);

    public object VisitNullable<T>(NullableSchema<T> schema)
        where T : struct =>
        new NullableProtoValueReader<T>(inner.CompileValue(schema.TargetSchema, traits));

    public object VisitStreamingBlob(Schema<Stream> schema) => inner.CompileValue(schema);

    public object VisitEventStream<TEvent>(EventStreamSchema<TEvent> schema) =>
        inner.CompileValue(schema);

    public object VisitList<TCollection, TElement, TBuilder>(
        IListSchema<TCollection, TElement, TBuilder> schema
    ) => inner.CompileValue((Schema<TCollection>)schema);

    public object VisitMap<TDictionary, TValue, TBuilder>(
        IMapSchema<TDictionary, TValue, TBuilder> schema
    ) => inner.CompileValue((Schema<TDictionary>)schema);

    public object VisitStruct<T, TBuilder>(IStructSchema<T, TBuilder> schema) =>
        inner.CompileValue((Schema<T>)schema);

    public object VisitUnion<T>(IUnionSchema<T> schema) => inner.CompileValue((Schema<T>)schema);

    public object VisitStringEnum<T>(StringEnumSchema<T> schema)
        where T : IStringEnumValue<T> => new ScalarProtoValueReader<T>(schema, traits);

    public object VisitIntEnum<T>(IntEnumSchema<T> schema)
        where T : struct, Enum => new ScalarProtoValueReader<T>(schema, traits);
}

internal sealed class DeferredProtoValueReader<T>
    : IProtoValueReader<T>,
        IDeferredCompilation<IProtoValueReader<T>>
{
    public void Complete(IProtoValueReader<T> compiled) => Set(compiled);

    private IProtoValueReader<T>? inner;

    public void Set(IProtoValueReader<T> reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        inner = reader;
    }

    public T ReadBody(ref ProtoReader reader, WireType wireType)
    {
        if (inner is null)
        {
            throw new InvalidOperationException("Proto reader has not been initialized.");
        }

        return inner.ReadBody(ref reader, wireType);
    }
}

internal sealed class ScalarProtoValueReader<T>(
    Schema<T> schema,
    IReadOnlyDictionary<ShapeId, Trait> traits
) : IProtoValueReader<T>
{
    public T ReadBody(ref ProtoReader reader, WireType wireType)
    {
        var resolved = ProtoWire.Unwrap(schema);
        return resolved.Kind switch
        {
            ShapeKind.Boolean => (T)(object)(reader.ReadVarint() != 0),
            ShapeKind.Byte => (T)
                (object)(sbyte)ProtoWire.ReadInteger(ref reader, resolved.Kind, traits),
            ShapeKind.Short => (T)
                (object)(short)ProtoWire.ReadInteger(ref reader, resolved.Kind, traits),
            ShapeKind.Integer => (T)
                (object)(int)ProtoWire.ReadInteger(ref reader, resolved.Kind, traits),
            ShapeKind.Long => (T)(object)ProtoWire.ReadInteger(ref reader, resolved.Kind, traits),
            ShapeKind.Float => (T)(object)BitConverter.UInt32BitsToSingle(reader.ReadFixed32()),
            ShapeKind.Double => (T)(object)BitConverter.UInt64BitsToDouble(reader.ReadFixed64()),
            ShapeKind.String => (T)(object)Encoding.UTF8.GetString(reader.ReadLengthDelimited()),
            ShapeKind.Blob => (T)(object)reader.ReadLengthDelimited().ToArray(),
            ShapeKind.BigInteger => (T)
                (object)
                    BigInteger.Parse(
                        Encoding.UTF8.GetString(reader.ReadLengthDelimited()),
                        CultureInfo.InvariantCulture
                    ),
            ShapeKind.BigDecimal => (T)
                (object)
                    decimal.Parse(
                        Encoding.UTF8.GetString(reader.ReadLengthDelimited()),
                        CultureInfo.InvariantCulture
                    ),
            ShapeKind.IntEnum => (T)
                ((IIntEnumSchema)resolved).CreateObject((int)reader.ReadVarint()),
            ShapeKind.Timestamp => (T)
                (object)ProtoWire.DecodeTimestamp(reader.ReadLengthDelimited()),
            ShapeKind.Enum => ReadStringEnum(resolved, ref reader),
            _ => throw new NotSupportedException(
                $"Proto codec cannot decode schema kind '{resolved.Kind}' (wire type {wireType})."
            ),
        };
    }

    private static T ReadStringEnum(Schema schema, ref ProtoReader reader)
    {
        var name = ProtoWire.EnumValueForOrdinal(schema, (int)reader.ReadVarint());
        return name is null ? default! : (T)((IStringEnumSchema)schema).CreateObject(name);
    }
}

internal sealed class NullableProtoValueReader<T>(IProtoValueReader<T> inner)
    : IProtoValueReader<T?>
    where T : struct
{
    public T? ReadBody(ref ProtoReader reader, WireType wireType) =>
        inner.ReadBody(ref reader, wireType);
}

internal sealed class MessageProtoValueReader<T>(IProtoMessageReader<T> messageReader)
    : IProtoValueReader<T>
{
    public T ReadBody(ref ProtoReader reader, WireType wireType) =>
        messageReader.Read(reader.ReadLengthDelimited());
}

internal sealed class DocumentProtoValueReader : IProtoValueReader<Document>
{
    public Document ReadBody(ref ProtoReader reader, WireType wireType) =>
        ProtoWire.DecodeDocumentValue(reader.ReadLengthDelimited());
}

internal sealed class ProtoFieldReaderCompiler<TContainer, TBuilder>(
    ProtoValueReaderCompiler compiler
) : IMemberVisitor<TContainer, TBuilder>
{
    private readonly List<IProtoFieldReader<TBuilder>> readers = [];
    private int stateSlotCount;

    public IReadOnlyList<IProtoFieldReader<TBuilder>> Readers => readers;

    public int StateSlotCount => stateSlotCount;

    public void Visit<TValue>(IMemberSchema<TContainer, TBuilder, TValue> member)
    {
        var target = ProtoWire.Unwrap(member.TargetSchema);
        if (target is IUnionSchema union && ProtoWire.IsInlinedUnion(target))
        {
            AddInlinedOneOfReaders(member, (dynamic)union);
            return;
        }

        var fieldNumber = ProtoWire.FieldNumber(member.Id, member.MemberTraits, readers.Count);
        readers.Add(
            target switch
            {
                IListSchema list => CreateListReader(
                    member,
                    (dynamic)list,
                    fieldNumber,
                    stateSlotCount++
                ),
                IMapSchema map => CreateMapReader(
                    member,
                    (dynamic)map,
                    fieldNumber,
                    stateSlotCount++
                ),
                _ => new ValueProtoFieldReader<TContainer, TBuilder, TValue>(
                    member,
                    fieldNumber,
                    compiler.CompileValue(member.TargetSchema, member.MemberTraits)
                ),
            }
        );
    }

    private void AddInlinedOneOfReaders<TUnion>(
        IMemberSchema<TContainer, TBuilder, TUnion> member,
        IUnionSchema<TUnion> union
    )
    {
        var visitor = new InlinedProtoUnionCaseReaderCompiler<TContainer, TBuilder, TUnion>(
            member,
            compiler,
            readers
        );
        union.VisitCases(visitor);
    }

    private ListProtoFieldReader<
        TContainer,
        TBuilder,
        TValue,
        TElement,
        TCollectionBuilder
    > CreateListReader<TValue, TElement, TCollectionBuilder>(
        IMemberSchema<TContainer, TBuilder, TValue> member,
        IListSchema<TValue, TElement, TCollectionBuilder> list,
        int fieldNumber,
        int stateSlot
    ) =>
        new(
            member,
            list,
            fieldNumber,
            stateSlot,
            compiler.CompileValue(
                list.TypedElementMember.TargetSchema,
                list.TypedElementMember.MemberTraits
            )
        );

    private MapProtoFieldReader<
        TContainer,
        TBuilder,
        TValue,
        TMapValue,
        TMapBuilder
    > CreateMapReader<TValue, TMapValue, TMapBuilder>(
        IMemberSchema<TContainer, TBuilder, TValue> member,
        IMapSchema<TValue, TMapValue, TMapBuilder> map,
        int fieldNumber,
        int stateSlot
    ) =>
        new(
            member,
            map,
            fieldNumber,
            stateSlot,
            ProtoWire.IsSparse((Schema)map),
            compiler.CompileValue(
                map.TypedValueMember.TargetSchema,
                map.TypedValueMember.MemberTraits
            )
        );
}

internal sealed class ValueProtoFieldReader<TContainer, TBuilder, TValue>(
    IMemberSchema<TContainer, TBuilder, TValue> member,
    int fieldNumber,
    IProtoValueReader<TValue> valueReader
) : IProtoFieldReader<TBuilder>
{
    public int FieldNumber { get; } = fieldNumber;

    public void ReadInto(
        TBuilder builder,
        ref ProtoReader reader,
        WireType wireType,
        ref ProtoReadState state
    )
    {
        try
        {
            member.SetValue(builder, valueReader.ReadBody(ref reader, wireType));
        }
        catch (MissingRequiredMemberException exception)
        {
            exception.PrependPathToken(member.Name);
            throw;
        }
    }
}

internal sealed class ListProtoFieldReader<
    TContainer,
    TBuilder,
    TCollection,
    TElement,
    TCollectionBuilder
>(
    IMemberSchema<TContainer, TBuilder, TCollection> member,
    IListSchema<TCollection, TElement, TCollectionBuilder> list,
    int fieldNumber,
    int stateSlot,
    IProtoValueReader<TElement> elementReader
) : IProtoFieldReader<TBuilder>, IProtoFieldCompleter<TBuilder>
{
    public int FieldNumber { get; } = fieldNumber;

    public void ReadInto(
        TBuilder builder,
        ref ProtoReader reader,
        WireType wireType,
        ref ProtoReadState state
    )
    {
        var accumulator = state.GetOrCreate(stateSlot, list.CreateTypedBuilder);
        var element = ProtoWire.Unwrap(list.ElementSchema);
        if (wireType == WireType.Len && ProtoWire.IsPackableScalar(element.Kind))
        {
            var packed = new ProtoReader(reader.ReadLengthDelimited());
            while (!packed.End)
            {
                list.Add(
                    accumulator,
                    elementReader.ReadBody(
                        ref packed,
                        ProtoWire.WireTypeOf(element.Kind, list.TypedElementMember.MemberTraits)
                    )
                );
            }
            return;
        }

        list.Add(accumulator, elementReader.ReadBody(ref reader, wireType));
    }

    public void Complete(TBuilder builder, ref ProtoReadState state)
    {
        var accumulator = state.TryGet<TCollectionBuilder>(stateSlot, out var existing)
            ? existing
            : list.CreateTypedBuilder();
        member.SetValue(builder, list.Build(accumulator));
    }
}

internal sealed class MapProtoFieldReader<TContainer, TBuilder, TDictionary, TValue, TMapBuilder>(
    IMemberSchema<TContainer, TBuilder, TDictionary> member,
    IMapSchema<TDictionary, TValue, TMapBuilder> map,
    int fieldNumber,
    int stateSlot,
    bool sparse,
    IProtoValueReader<TValue> valueReader
) : IProtoFieldReader<TBuilder>, IProtoFieldCompleter<TBuilder>
{
    public int FieldNumber { get; } = fieldNumber;

    private readonly SparseScalarValueReader<TValue> sparseReader = new(
        map.TypedValueMember.TargetSchema
    );

    public void ReadInto(
        TBuilder builder,
        ref ProtoReader reader,
        WireType wireType,
        ref ProtoReadState state
    )
    {
        var accumulator = state.GetOrCreate(stateSlot, map.CreateTypedBuilder);
        ReadMapEntry(accumulator, reader.ReadLengthDelimited());
    }

    public void Complete(TBuilder builder, ref ProtoReadState state)
    {
        var accumulator = state.TryGet<TMapBuilder>(stateSlot, out var existing)
            ? existing
            : map.CreateTypedBuilder();
        member.SetValue(builder, map.Build(accumulator));
    }

    private void ReadMapEntry(TMapBuilder builder, ReadOnlySpan<byte> bytes)
    {
        var key = string.Empty;
        TValue? value = default;
        var reader = new ProtoReader(bytes);
        while (!reader.End)
        {
            var (number, wireType) = reader.ReadTag();
            switch (number)
            {
                case 1:
                    key = Encoding.UTF8.GetString(reader.ReadLengthDelimited());
                    break;
                case 2 when sparse:
                    value = sparseReader.Read(reader.ReadLengthDelimited());
                    break;
                case 2:
                    value = valueReader.ReadBody(ref reader, wireType);
                    break;
                default:
                    reader.SkipField(wireType);
                    break;
            }
        }

        map.Add(builder, key, value!);
    }
}

internal sealed class SparseScalarValueReader<T>(Schema<T> schema)
{
    public T? Read(ReadOnlySpan<byte> bytes) => ProtoWire.DecodeScalarValueMessage(schema, bytes);
}

internal sealed class InlinedProtoUnionCaseReaderCompiler<TContainer, TBuilder, TUnion>(
    IMemberSchema<TContainer, TBuilder, TUnion> member,
    ProtoValueReaderCompiler compiler,
    List<IProtoFieldReader<TBuilder>> readers
) : IUnionCaseVisitor<TUnion>
{
    public void Visit<TValue>(IUnionCaseSchema<TUnion, TValue> unionCase) =>
        readers.Add(
            new InlinedProtoUnionCaseReader<TContainer, TBuilder, TUnion, TValue>(
                member,
                unionCase,
                ProtoWire.FieldNumber(unionCase.Id, unionCase.Traits, readers.Count),
                compiler.CompileValue(unionCase.TargetSchema, unionCase.Traits)
            )
        );
}

internal sealed class InlinedProtoUnionCaseReader<TContainer, TBuilder, TUnion, TValue>(
    IMemberSchema<TContainer, TBuilder, TUnion> member,
    IUnionCaseSchema<TUnion, TValue> unionCase,
    int fieldNumber,
    IProtoValueReader<TValue> valueReader
) : IProtoFieldReader<TBuilder>
{
    public int FieldNumber { get; } = fieldNumber;

    public void ReadInto(
        TBuilder builder,
        ref ProtoReader reader,
        WireType wireType,
        ref ProtoReadState state
    )
    {
        member.SetValue(builder, unionCase.Create(valueReader.ReadBody(ref reader, wireType)));
    }
}

internal sealed class StructureProtoMessageReader<T, TBuilder> : IProtoMessageReader<T>
{
    private const int MaxDenseFieldNumber = 256;

    private readonly Func<TBuilder> createBuilder;
    private readonly Func<TBuilder, T> build;
    private readonly IReadOnlyList<IProtoFieldCompleter<TBuilder>> completers;
    private readonly IProtoFieldReader<TBuilder>?[]? denseReaders;
    private readonly Dictionary<int, IProtoFieldReader<TBuilder>>? sparseReaders;
    private readonly int stateSlotCount;

    public StructureProtoMessageReader(
        Func<TBuilder> createBuilder,
        Func<TBuilder, T> build,
        IReadOnlyList<IProtoFieldReader<TBuilder>> fieldReaders,
        int stateSlotCount
    )
    {
        this.createBuilder = createBuilder;
        this.build = build;
        this.stateSlotCount = stateSlotCount;
        completers = fieldReaders.OfType<IProtoFieldCompleter<TBuilder>>().ToArray();

        var maxFieldNumber =
            fieldReaders.Count == 0 ? 0 : fieldReaders.Max(reader => reader.FieldNumber);
        if (maxFieldNumber <= MaxDenseFieldNumber)
        {
            denseReaders = new IProtoFieldReader<TBuilder>?[maxFieldNumber + 1];
            foreach (var fieldReader in fieldReaders)
            {
                if (denseReaders[fieldReader.FieldNumber] is not null)
                {
                    throw new InvalidOperationException(
                        $"Duplicate protobuf field number {fieldReader.FieldNumber}."
                    );
                }

                denseReaders[fieldReader.FieldNumber] = fieldReader;
            }
        }
        else
        {
            sparseReaders = fieldReaders.ToDictionary(reader => reader.FieldNumber);
        }
    }

    public T Read(ReadOnlySpan<byte> bytes)
    {
        var builder = createBuilder();
        var state = new ProtoReadState(stateSlotCount);
        var reader = new ProtoReader(bytes);
        while (!reader.End)
        {
            var (number, wireType) = reader.ReadTag();
            if (TryGetFieldReader(number, out var fieldReader))
            {
                fieldReader.ReadInto(builder, ref reader, wireType, ref state);
            }
            else
            {
                reader.SkipField(wireType);
            }
        }

        foreach (var completer in completers)
        {
            completer.Complete(builder, ref state);
        }

        return build(builder);
    }

    private bool TryGetFieldReader(int fieldNumber, out IProtoFieldReader<TBuilder> fieldReader)
    {
        if (denseReaders is not null)
        {
            if (
                (uint)fieldNumber < (uint)denseReaders.Length
                && denseReaders[fieldNumber] is { } denseReader
            )
            {
                fieldReader = denseReader;
                return true;
            }

            fieldReader = default!;
            return false;
        }

        return sparseReaders!.TryGetValue(fieldNumber, out fieldReader!);
    }
}

internal sealed class ProtoUnionCaseReaderCompiler<TUnion>(ProtoValueReaderCompiler compiler)
    : IUnionCaseVisitor<TUnion>
{
    private readonly List<IProtoUnionCaseReader<TUnion>> readers = [];

    public IReadOnlyList<IProtoUnionCaseReader<TUnion>> Readers => readers;

    public void Visit<TValue>(IUnionCaseSchema<TUnion, TValue> unionCase) =>
        readers.Add(
            new ProtoUnionCaseReader<TUnion, TValue>(
                unionCase,
                ProtoWire.FieldNumber(unionCase.Id, unionCase.Traits, readers.Count),
                compiler.CompileValue(unionCase.TargetSchema, unionCase.Traits)
            )
        );
}

internal sealed class ProtoUnionCaseReader<TUnion, TValue>(
    IUnionCaseSchema<TUnion, TValue> unionCase,
    int fieldNumber,
    IProtoValueReader<TValue> valueReader
) : IProtoUnionCaseReader<TUnion>
{
    public int FieldNumber { get; } = fieldNumber;

    public TUnion Read(ref ProtoReader reader, WireType wireType) =>
        unionCase.Create(valueReader.ReadBody(ref reader, wireType));
}

internal sealed class UnionProtoMessageReader<T>(
    IReadOnlyList<IProtoUnionCaseReader<T>> caseReaders
) : IProtoMessageReader<T>
{
    private readonly Dictionary<int, IProtoUnionCaseReader<T>> readersByField =
        caseReaders.ToDictionary(reader => reader.FieldNumber);

    public T Read(ReadOnlySpan<byte> bytes)
    {
        var reader = new ProtoReader(bytes);
        T? result = default;
        while (!reader.End)
        {
            var (number, wireType) = reader.ReadTag();
            if (readersByField.TryGetValue(number, out var caseReader))
            {
                result = caseReader.Read(ref reader, wireType);
            }
            else
            {
                reader.SkipField(wireType);
            }
        }

        return result!;
    }
}
