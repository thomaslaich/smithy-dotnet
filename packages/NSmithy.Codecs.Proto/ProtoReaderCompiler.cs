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

        return cache.GetOrCompile(
            resolved,
            static () => new DeferredProtoValueReader<T>(),
            target => (IProtoValueReader<T>)target.Accept(this)
        );
    }

    public object VisitBoolean(Schema<bool> schema) => new BooleanProtoValueReader();

    public object VisitByte(Schema<sbyte> schema) =>
        new ByteProtoValueReader(ShapeKind.Byte, EmptyTraits);

    public object VisitShort(Schema<short> schema) =>
        new ShortProtoValueReader(ShapeKind.Short, EmptyTraits);

    public object VisitInteger(Schema<int> schema) =>
        new IntegerProtoValueReader(ShapeKind.Integer, EmptyTraits);

    public object VisitLong(Schema<long> schema) =>
        new LongProtoValueReader(ShapeKind.Long, EmptyTraits);

    public object VisitFloat(Schema<float> schema) => new FloatProtoValueReader();

    public object VisitDouble(Schema<double> schema) => new DoubleProtoValueReader();

    public object VisitBigInteger(Schema<BigInteger> schema) => new BigIntegerProtoValueReader();

    public object VisitBigDecimal(Schema<decimal> schema) => new BigDecimalProtoValueReader();

    public object VisitString(Schema<string> schema) => new StringProtoValueReader();

    public object VisitBlob(Schema<byte[]> schema) => new BlobProtoValueReader();

    public object VisitTimestamp(Schema<DateTimeOffset> schema) => new TimestampProtoValueReader();

    public object VisitDocument(Schema<Document> schema) => new DocumentProtoValueReader();

    public object VisitNullable<T>(NullableSchema<T> schema)
        where T : struct => new NullableProtoValueReader<T>(CompileValue(schema.TypedTarget));

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
        where T : IStringEnumValue<T> => new StringEnumProtoValueReader<T>(schema);

    public object VisitIntEnum<T>(IntEnumSchema<T> schema)
        where T : struct, Enum => new IntEnumProtoValueReader<T>(schema);

    private static object UnsupportedAggregateValue() =>
        throw new NotSupportedException(
            "Proto codec cannot decode list or map schemas as standalone protobuf values."
        );
}

internal sealed class MessageReaderVisitor(ProtoValueReaderCompiler compiler)
    : PartialSchemaVisitor<object>
{
    public override object VisitStruct<T, TBuilder>(IStructSchema<T, TBuilder> schema)
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

    public override object VisitUnion<T>(IUnionSchema<T> schema)
    {
        var visitor = new ProtoUnionCaseReaderCompiler<T>(compiler);
        schema.VisitCases(visitor);
        return new UnionProtoMessageReader<T>(visitor.Readers);
    }

    protected override object VisitDefault(Schema schema) =>
        throw new InvalidOperationException(
            "Protobuf messages must be backed by a structure or union schema."
        );
}

internal sealed class MemberTraitProtoReaderCompiler(
    ProtoValueReaderCompiler inner,
    IReadOnlyDictionary<ShapeId, Trait> traits
) : ISchemaVisitor<object>
{
    public object VisitBoolean(Schema<bool> schema) => new BooleanProtoValueReader();

    public object VisitByte(Schema<sbyte> schema) =>
        new ByteProtoValueReader(ShapeKind.Byte, traits);

    public object VisitShort(Schema<short> schema) =>
        new ShortProtoValueReader(ShapeKind.Short, traits);

    public object VisitInteger(Schema<int> schema) =>
        new IntegerProtoValueReader(ShapeKind.Integer, traits);

    public object VisitLong(Schema<long> schema) =>
        new LongProtoValueReader(ShapeKind.Long, traits);

    public object VisitFloat(Schema<float> schema) => new FloatProtoValueReader();

    public object VisitDouble(Schema<double> schema) => new DoubleProtoValueReader();

    public object VisitBigInteger(Schema<BigInteger> schema) => new BigIntegerProtoValueReader();

    public object VisitBigDecimal(Schema<decimal> schema) => new BigDecimalProtoValueReader();

    public object VisitString(Schema<string> schema) => new StringProtoValueReader();

    public object VisitBlob(Schema<byte[]> schema) => new BlobProtoValueReader();

    public object VisitTimestamp(Schema<DateTimeOffset> schema) => new TimestampProtoValueReader();

    public object VisitDocument(Schema<Document> schema) => inner.CompileValue(schema);

    public object VisitNullable<T>(NullableSchema<T> schema)
        where T : struct =>
        new NullableProtoValueReader<T>(inner.CompileValue(schema.TypedTarget, traits));

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
        where T : IStringEnumValue<T> => new StringEnumProtoValueReader<T>(schema);

    public object VisitIntEnum<T>(IntEnumSchema<T> schema)
        where T : struct, Enum => new IntEnumProtoValueReader<T>(schema);
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

internal sealed class BooleanProtoValueReader : IProtoValueReader<bool>
{
    public bool ReadBody(ref ProtoReader reader, WireType wireType) => reader.ReadVarint() != 0;
}

internal sealed class ByteProtoValueReader(
    ShapeKind kind,
    IReadOnlyDictionary<ShapeId, Trait> traits
) : IProtoValueReader<sbyte>
{
    private readonly ProtoWire.IntEncoding encoding = ProtoWire.IntEncodingOf(kind, traits);

    public sbyte ReadBody(ref ProtoReader reader, WireType wireType) =>
        (sbyte)ProtoWire.ReadInteger(ref reader, encoding);
}

internal sealed class ShortProtoValueReader(
    ShapeKind kind,
    IReadOnlyDictionary<ShapeId, Trait> traits
) : IProtoValueReader<short>
{
    private readonly ProtoWire.IntEncoding encoding = ProtoWire.IntEncodingOf(kind, traits);

    public short ReadBody(ref ProtoReader reader, WireType wireType) =>
        (short)ProtoWire.ReadInteger(ref reader, encoding);
}

internal sealed class IntegerProtoValueReader(
    ShapeKind kind,
    IReadOnlyDictionary<ShapeId, Trait> traits
) : IProtoValueReader<int>
{
    private readonly ProtoWire.IntEncoding encoding = ProtoWire.IntEncodingOf(kind, traits);

    public int ReadBody(ref ProtoReader reader, WireType wireType) =>
        (int)ProtoWire.ReadInteger(ref reader, encoding);
}

internal sealed class LongProtoValueReader(
    ShapeKind kind,
    IReadOnlyDictionary<ShapeId, Trait> traits
) : IProtoValueReader<long>
{
    private readonly ProtoWire.IntEncoding encoding = ProtoWire.IntEncodingOf(kind, traits);

    public long ReadBody(ref ProtoReader reader, WireType wireType) =>
        ProtoWire.ReadInteger(ref reader, encoding);
}

internal sealed class FloatProtoValueReader : IProtoValueReader<float>
{
    public float ReadBody(ref ProtoReader reader, WireType wireType) =>
        BitConverter.UInt32BitsToSingle(reader.ReadFixed32());
}

internal sealed class DoubleProtoValueReader : IProtoValueReader<double>
{
    public double ReadBody(ref ProtoReader reader, WireType wireType) =>
        BitConverter.UInt64BitsToDouble(reader.ReadFixed64());
}

internal sealed class StringProtoValueReader : IProtoValueReader<string>
{
    public string ReadBody(ref ProtoReader reader, WireType wireType) =>
        Encoding.UTF8.GetString(reader.ReadLengthDelimited());
}

internal sealed class BlobProtoValueReader : IProtoValueReader<byte[]>
{
    public byte[] ReadBody(ref ProtoReader reader, WireType wireType) =>
        reader.ReadLengthDelimited().ToArray();
}

internal sealed class BigIntegerProtoValueReader : IProtoValueReader<BigInteger>
{
    public BigInteger ReadBody(ref ProtoReader reader, WireType wireType) =>
        BigInteger.Parse(
            Encoding.UTF8.GetString(reader.ReadLengthDelimited()),
            CultureInfo.InvariantCulture
        );
}

internal sealed class BigDecimalProtoValueReader : IProtoValueReader<decimal>
{
    public decimal ReadBody(ref ProtoReader reader, WireType wireType) =>
        decimal.Parse(
            Encoding.UTF8.GetString(reader.ReadLengthDelimited()),
            CultureInfo.InvariantCulture
        );
}

internal sealed class TimestampProtoValueReader : IProtoValueReader<DateTimeOffset>
{
    public DateTimeOffset ReadBody(ref ProtoReader reader, WireType wireType) =>
        ProtoWire.DecodeTimestamp(reader.ReadLengthDelimited());
}

internal sealed class StringEnumProtoValueReader<T>(StringEnumSchema<T> schema)
    : IProtoValueReader<T>
    where T : IStringEnumValue<T>
{
    private readonly string[] values = ProtoWire.EnumValues(schema);

    // 0 is the synthetic proto UNSPECIFIED, which has no Smithy enum member.
    public T ReadBody(ref ProtoReader reader, WireType wireType)
    {
        var ordinal = (int)reader.ReadVarint();
        return ordinal > 0 && ordinal <= values.Length
            ? schema.Create(values[ordinal - 1])
            : default!;
    }
}

internal sealed class IntEnumProtoValueReader<T>(IntEnumSchema<T> schema) : IProtoValueReader<T>
    where T : struct, Enum
{
    public T ReadBody(ref ProtoReader reader, WireType wireType) =>
        schema.Create((int)reader.ReadVarint());
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
        var target = ProtoWire.Unwrap(member.TypedTarget);
        if (ProtoWire.IsInlinedUnion(target))
        {
            target.Accept(new InlinedOneOfCompiler<TValue>(this, member));
            return;
        }

        var fieldNumber = ProtoWire.FieldNumber(member.Id, member.MemberTraits, readers.Count);
        readers.Add(target.Accept(new ReaderCompiler<TValue>(this, member, fieldNumber)));
    }

    // A repeated or map field's element type only comes into scope by visiting the target.
    private sealed class ReaderCompiler<TValue>(
        ProtoFieldReaderCompiler<TContainer, TBuilder> owner,
        IMemberSchema<TContainer, TBuilder, TValue> member,
        int fieldNumber
    ) : PartialSchemaVisitor<IProtoFieldReader<TBuilder>>
    {
        public override IProtoFieldReader<TBuilder> VisitList<
            TCollection,
            TElement,
            TCollectionBuilder
        >(IListSchema<TCollection, TElement, TCollectionBuilder> schema) =>
            owner.CreateListReader(
                member,
                (IListSchema<TValue, TElement, TCollectionBuilder>)(object)schema,
                fieldNumber,
                owner.stateSlotCount++
            );

        public override IProtoFieldReader<TBuilder> VisitMap<TDictionary, TMapValue, TMapBuilder>(
            IMapSchema<TDictionary, TMapValue, TMapBuilder> schema
        ) =>
            owner.CreateMapReader(
                member,
                (IMapSchema<TValue, TMapValue, TMapBuilder>)(object)schema,
                fieldNumber,
                owner.stateSlotCount++
            );

        protected override IProtoFieldReader<TBuilder> VisitDefault(Schema schema) =>
            owner.CreateValueReader(member, fieldNumber);
    }

    private sealed class InlinedOneOfCompiler<TValue>(
        ProtoFieldReaderCompiler<TContainer, TBuilder> owner,
        IMemberSchema<TContainer, TBuilder, TValue> member
    ) : PartialSchemaVisitor<object?>
    {
        public override object? VisitUnion<TUnion>(IUnionSchema<TUnion> schema)
        {
            owner.AddInlinedOneOfReaders(
                (IMemberSchema<TContainer, TBuilder, TUnion>)(object)member,
                schema
            );
            return null;
        }
    }

    private ValueProtoFieldReader<TContainer, TBuilder, TValue> CreateValueReader<TValue>(
        IMemberSchema<TContainer, TBuilder, TValue> member,
        int fieldNumber
    ) => new(member, fieldNumber, compiler.CompileValue(member.TypedTarget, member.MemberTraits));

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
                list.TypedElementMember.TypedTarget,
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
                map.TypedValueMember.TypedTarget,
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
    private readonly Func<TCollectionBuilder> createBuilder = list.CreateTypedBuilder;
    private readonly Action<TCollectionBuilder, TElement> add = list.Add;
    private readonly Func<TCollectionBuilder, TCollection> build = list.Build;
    private readonly bool packable = ProtoWire.IsPackableScalar(
        ProtoWire.Unwrap(list.ElementSchema).Kind
    );
    private readonly WireType elementWireType = ProtoWire.WireTypeOf(
        ProtoWire.Unwrap(list.ElementSchema).Kind,
        list.TypedElementMember.MemberTraits
    );

    public int FieldNumber { get; } = fieldNumber;

    public void ReadInto(
        TBuilder builder,
        ref ProtoReader reader,
        WireType wireType,
        ref ProtoReadState state
    )
    {
        var accumulator = state.GetOrCreate(stateSlot, createBuilder);
        if (wireType == WireType.Len && packable)
        {
            var packed = new ProtoReader(reader.ReadLengthDelimited());
            while (!packed.End)
            {
                add(accumulator, elementReader.ReadBody(ref packed, elementWireType));
            }
            return;
        }

        add(accumulator, elementReader.ReadBody(ref reader, wireType));
    }

    public void Complete(TBuilder builder, ref ProtoReadState state)
    {
        var accumulator = state.TryGet<TCollectionBuilder>(stateSlot, out var existing)
            ? existing
            : createBuilder();
        member.SetValue(builder, build(accumulator));
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
    private readonly Func<TMapBuilder> createBuilder = map.CreateTypedBuilder;
    private readonly Action<TMapBuilder, string, TValue> add = map.Add;
    private readonly Func<TMapBuilder, TDictionary> build = map.Build;
    public int FieldNumber { get; } = fieldNumber;

    private readonly SparseScalarValueReader<TValue> sparseReader = new(
        map.TypedValueMember.TypedTarget
    );

    public void ReadInto(
        TBuilder builder,
        ref ProtoReader reader,
        WireType wireType,
        ref ProtoReadState state
    )
    {
        var accumulator = state.GetOrCreate(stateSlot, createBuilder);
        ReadMapEntry(accumulator, reader.ReadLengthDelimited());
    }

    public void Complete(TBuilder builder, ref ProtoReadState state)
    {
        var accumulator = state.TryGet<TMapBuilder>(stateSlot, out var existing)
            ? existing
            : createBuilder();
        member.SetValue(builder, build(accumulator));
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

        add(builder, key, value!);
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
