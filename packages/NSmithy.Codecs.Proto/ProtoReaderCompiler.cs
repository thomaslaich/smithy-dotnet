using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Proto;

internal interface IProtoMessageReader<out T>
{
    T Read(ReadOnlySpan<byte> bytes);
}

internal interface IProtoFieldReader<in TBuilder>
{
    int FieldNumber { get; }

    void ReadInto(
        TBuilder builder,
        ref ProtoReader reader,
        WireType wireType,
        ProtoReadState state
    );

    void Complete(TBuilder builder, ProtoReadState state);
}

internal interface IProtoUnionCaseReader<out TUnion>
{
    int FieldNumber { get; }

    TUnion Read(ref ProtoReader reader, WireType wireType);
}

internal sealed class ProtoReadState
{
    private readonly Dictionary<int, object> builders = [];
    private readonly HashSet<int> seen = [];

    public bool WasSeen(int fieldNumber) => seen.Contains(fieldNumber);

    public TBuilder GetOrCreate<TBuilder>(int fieldNumber, Func<TBuilder> create)
    {
        seen.Add(fieldNumber);
        if (builders.TryGetValue(fieldNumber, out var builder))
        {
            return (TBuilder)builder;
        }

        var created = create();
        builders.Add(fieldNumber, created!);
        return created;
    }
}

internal static class ProtoReaderCompiler
{
    public static IProtoMessageReader<T> Compile<T>(Schema<T> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var unwrapped = ProtoWire.Unwrap(schema);
        return (IProtoMessageReader<T>)unwrapped.Accept(new Visitor());
    }

    private sealed class Visitor : ISchemaVisitor<object>
    {
        public object VisitBoolean(Schema<bool> schema) => Unsupported();

        public object VisitByte(Schema<sbyte> schema) => Unsupported();

        public object VisitShort(Schema<short> schema) => Unsupported();

        public object VisitInteger(Schema<int> schema) => Unsupported();

        public object VisitLong(Schema<long> schema) => Unsupported();

        public object VisitFloat(Schema<float> schema) => Unsupported();

        public object VisitDouble(Schema<double> schema) => Unsupported();

        public object VisitBigInteger(Schema<System.Numerics.BigInteger> schema) => Unsupported();

        public object VisitBigDecimal(Schema<decimal> schema) => Unsupported();

        public object VisitString(Schema<string> schema) => Unsupported();

        public object VisitBlob(Schema<byte[]> schema) => Unsupported();

        public object VisitTimestamp(Schema<DateTimeOffset> schema) => Unsupported();

        public object VisitDocument(Schema<Document> schema) => Unsupported();

        public object VisitNullable<T>(NullableSchema<T> schema)
            where T : struct => Unsupported();

        public object VisitEventStream<TEvent>(EventStreamSchema<TEvent> schema) => Unsupported();

        public object VisitList<TCollection, TElement, TBuilder>(
            IListSchema<TCollection, TElement, TBuilder> schema
        ) => Unsupported();

        public object VisitMap<TDictionary, TValue, TBuilder>(
            IMapSchema<TDictionary, TValue, TBuilder> schema
        ) => Unsupported();

        public object VisitStruct<T, TBuilder>(IStructSchema<T, TBuilder> schema)
        {
            var visitor = new ProtoFieldReaderCompiler<T, TBuilder>();
            schema.VisitMembers(visitor);
            return new StructureProtoMessageReader<T, TBuilder>(
                schema.CreateTypedBuilder,
                schema.Build,
                visitor.Readers
            );
        }

        public object VisitUnion<T>(IUnionSchema<T> schema)
        {
            var visitor = new ProtoUnionCaseReaderCompiler<T>();
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

    private static StructureProtoMessageReader<TWire, TBuilder> CompileUnwrapped<TWire, TBuilder>(
        StructSchema<TWire, TBuilder> schema
    )
    {
        var visitor = new ProtoFieldReaderCompiler<TWire, TBuilder>();
        schema.VisitMembers(visitor);
        return new StructureProtoMessageReader<TWire, TBuilder>(
            schema.CreateTypedBuilder,
            schema.Build,
            visitor.Readers
        );
    }

    private static StructureProtoMessageReader<SmithyUnit, SmithyUnit> CompileUnwrapped(
        UnitSchema schema
    ) => new(() => SmithyUnit.Value, static builder => builder, []);

    private static UnionProtoMessageReader<TWire> CompileUnwrapped<TWire>(UnionSchema<TWire> schema)
    {
        var visitor = new ProtoUnionCaseReaderCompiler<TWire>();
        schema.VisitCases(visitor);
        return new UnionProtoMessageReader<TWire>(visitor.Readers);
    }

    private static IProtoMessageReader<T> CompileUnwrapped<T>(Schema schema) =>
        throw new InvalidOperationException(
            "Protobuf messages must be backed by a structure or union schema."
        );
}

internal sealed class ProtoFieldReaderCompiler<TContainer, TBuilder>
    : IMemberVisitor<TContainer, TBuilder>
{
    private readonly List<IProtoFieldReader<TBuilder>> readers = [];

    public IReadOnlyList<IProtoFieldReader<TBuilder>> Readers => readers;

    public void Visit<TValue>(IMemberSchema<TContainer, TBuilder, TValue> member)
    {
        var target = ProtoWire.Unwrap(member.TargetSchema);
        if (
            target is IUnionSchema union
            && target.HasTrait(new ShapeId("alloy.proto", "protoInlinedOneOf"))
        )
        {
            AddInlinedOneOfReaders(member, (dynamic)union);
            return;
        }

        readers.Add(
            target switch
            {
                IListSchema list => CreateListReader(member, (dynamic)list),
                IMapSchema map => CreateMapReader(member, (dynamic)map),
                _ => new ValueProtoFieldReader<TContainer, TBuilder, TValue>(member),
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
            readers
        );
        union.VisitCases(visitor);
    }

    private static ListProtoFieldReader<
        TContainer,
        TBuilder,
        TValue,
        TCollection,
        TElement,
        TCollectionBuilder
    > CreateListReader<TValue, TCollection, TElement, TCollectionBuilder>(
        IMemberSchema<TContainer, TBuilder, TValue> member,
        IListSchema<TCollection, TElement, TCollectionBuilder> list
    ) =>
        new ListProtoFieldReader<
            TContainer,
            TBuilder,
            TValue,
            TCollection,
            TElement,
            TCollectionBuilder
        >(member, list);

    private static MapProtoFieldReader<
        TContainer,
        TBuilder,
        TValue,
        TDictionary,
        TMapValue,
        TMapBuilder
    > CreateMapReader<TValue, TDictionary, TMapValue, TMapBuilder>(
        IMemberSchema<TContainer, TBuilder, TValue> member,
        IMapSchema<TDictionary, TMapValue, TMapBuilder> map
    ) =>
        new MapProtoFieldReader<TContainer, TBuilder, TValue, TDictionary, TMapValue, TMapBuilder>(
            member,
            map,
            ((Schema)(object)map).HasTrait(new ShapeId("smithy.api", "sparse"))
        );
}

internal sealed class ValueProtoFieldReader<TContainer, TBuilder, TValue>(
    IMemberSchema<TContainer, TBuilder, TValue> member
) : IProtoFieldReader<TBuilder>
{
    public int FieldNumber { get; } = ProtoWire.ProtoIndex(member.MemberTraits);

    public void ReadInto(
        TBuilder builder,
        ref ProtoReader reader,
        WireType wireType,
        ProtoReadState state
    ) =>
        member.SetValue(
            builder,
            (TValue)
                ProtoWire.ReadValueBody(
                    ref reader,
                    member.TargetSchema,
                    member.MemberTraits,
                    wireType
                )!
        );

    public void Complete(TBuilder builder, ProtoReadState state) { }
}

internal sealed class ListProtoFieldReader<
    TContainer,
    TBuilder,
    TValue,
    TCollection,
    TElement,
    TCollectionBuilder
>(
    IMemberSchema<TContainer, TBuilder, TValue> member,
    IListSchema<TCollection, TElement, TCollectionBuilder> list
) : IProtoFieldReader<TBuilder>
{
    public int FieldNumber { get; } = ProtoWire.ProtoIndex(member.MemberTraits);

    public void ReadInto(
        TBuilder builder,
        ref ProtoReader reader,
        WireType wireType,
        ProtoReadState state
    )
    {
        var accumulator = state.GetOrCreate(FieldNumber, list.CreateTypedBuilder);
        var element = ProtoWire.Unwrap(list.ElementSchema);
        if (wireType == WireType.Len && ProtoWire.IsPackableScalar(element.Kind))
        {
            var packed = new ProtoReader(reader.ReadLengthDelimited());
            while (!packed.End)
            {
                list.Add(
                    accumulator,
                    (TElement)
                        ProtoWire.ReadValueBody(
                            ref packed,
                            list.ElementSchema,
                            member.MemberTraits,
                            ProtoWire.WireTypeOf(element.Kind, member.MemberTraits)
                        )!
                );
            }
            return;
        }

        list.Add(
            accumulator,
            (TElement)
                ProtoWire.ReadValueBody(
                    ref reader,
                    list.ElementSchema,
                    member.MemberTraits,
                    wireType
                )!
        );
    }

    public void Complete(TBuilder builder, ProtoReadState state)
    {
        var accumulator = state.WasSeen(FieldNumber)
            ? state.GetOrCreate(FieldNumber, list.CreateTypedBuilder)
            : list.CreateTypedBuilder();
        member.SetValue(builder, (TValue)(object)list.Build(accumulator)!);
    }
}

internal sealed class MapProtoFieldReader<
    TContainer,
    TBuilder,
    TValue,
    TDictionary,
    TMapValue,
    TMapBuilder
>(
    IMemberSchema<TContainer, TBuilder, TValue> member,
    IMapSchema<TDictionary, TMapValue, TMapBuilder> map,
    bool sparse
) : IProtoFieldReader<TBuilder>
{
    public int FieldNumber { get; } = ProtoWire.ProtoIndex(member.MemberTraits);

    public void ReadInto(
        TBuilder builder,
        ref ProtoReader reader,
        WireType wireType,
        ProtoReadState state
    )
    {
        var accumulator = state.GetOrCreate(FieldNumber, map.CreateTypedBuilder);
        var entryBytes = reader.ReadLengthDelimited();
        ProtoWire.ReadMapEntry(map, sparse, accumulator!, entryBytes);
    }

    public void Complete(TBuilder builder, ProtoReadState state)
    {
        var accumulator = state.WasSeen(FieldNumber)
            ? state.GetOrCreate(FieldNumber, map.CreateTypedBuilder)
            : map.CreateTypedBuilder();
        member.SetValue(builder, (TValue)(object)map.Build(accumulator)!);
    }
}

internal sealed class InlinedProtoUnionCaseReaderCompiler<TContainer, TBuilder, TUnion>(
    IMemberSchema<TContainer, TBuilder, TUnion> member,
    List<IProtoFieldReader<TBuilder>> readers
) : IUnionCaseVisitor<TUnion>
{
    public void Visit<TValue>(IUnionCaseSchema<TUnion, TValue> unionCase) =>
        readers.Add(
            new InlinedProtoUnionCaseReader<TContainer, TBuilder, TUnion, TValue>(member, unionCase)
        );
}

internal sealed class InlinedProtoUnionCaseReader<TContainer, TBuilder, TUnion, TValue>(
    IMemberSchema<TContainer, TBuilder, TUnion> member,
    IUnionCaseSchema<TUnion, TValue> unionCase
) : IProtoFieldReader<TBuilder>
{
    public int FieldNumber { get; } = ProtoWire.ProtoIndex(unionCase.Traits);

    public void ReadInto(
        TBuilder builder,
        ref ProtoReader reader,
        WireType wireType,
        ProtoReadState state
    )
    {
        var caseValue = (TValue)
            ProtoWire.ReadValueBody(
                ref reader,
                unionCase.TargetSchema,
                unionCase.Traits,
                wireType
            )!;
        member.SetValue(builder, unionCase.Create(caseValue));
    }

    public void Complete(TBuilder builder, ProtoReadState state) { }
}

internal sealed class StructureProtoMessageReader<T, TBuilder>(
    Func<TBuilder> createBuilder,
    Func<TBuilder, T> build,
    IReadOnlyList<IProtoFieldReader<TBuilder>> fieldReaders
) : IProtoMessageReader<T>
{
    private readonly Dictionary<int, IProtoFieldReader<TBuilder>> readersByField =
        fieldReaders.ToDictionary(reader => reader.FieldNumber);

    public T Read(ReadOnlySpan<byte> bytes)
    {
        var builder = createBuilder();
        var state = new ProtoReadState();
        var reader = new ProtoReader(bytes);
        while (!reader.End)
        {
            var (number, wireType) = reader.ReadTag();
            if (readersByField.TryGetValue(number, out var fieldReader))
            {
                fieldReader.ReadInto(builder, ref reader, wireType, state);
            }
            else
            {
                reader.SkipField(wireType);
            }
        }

        foreach (var fieldReader in fieldReaders)
        {
            fieldReader.Complete(builder, state);
        }

        return build(builder);
    }
}

internal sealed class ProtoUnionCaseReaderCompiler<TUnion> : IUnionCaseVisitor<TUnion>
{
    private readonly List<IProtoUnionCaseReader<TUnion>> readers = [];

    public IReadOnlyList<IProtoUnionCaseReader<TUnion>> Readers => readers;

    public void Visit<TValue>(IUnionCaseSchema<TUnion, TValue> unionCase) =>
        readers.Add(new ProtoUnionCaseReader<TUnion, TValue>(unionCase));
}

internal sealed class ProtoUnionCaseReader<TUnion, TValue>(
    IUnionCaseSchema<TUnion, TValue> unionCase
) : IProtoUnionCaseReader<TUnion>
{
    public int FieldNumber { get; } = ProtoWire.ProtoIndex(unionCase.Traits);

    public TUnion Read(ref ProtoReader reader, WireType wireType)
    {
        var value = (TValue)
            ProtoWire.ReadValueBody(
                ref reader,
                unionCase.TargetSchema,
                unionCase.Traits,
                wireType
            )!;
        return unionCase.Create(value);
    }
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
