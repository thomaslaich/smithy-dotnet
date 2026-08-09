using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Proto;

internal interface IProtoMessageWriter<in T>
{
    void Write(ProtoWriter writer, T value);
}

internal interface IProtoMemberWriter<in TContainer>
{
    void Write(ProtoWriter writer, TContainer value);
}

internal interface IProtoUnionCaseWriter<in TUnion>
{
    bool TryWrite(ProtoWriter writer, TUnion value);
}

internal static class ProtoWriterCompiler
{
    public static IProtoMessageWriter<T> Compile<T>(Schema<T> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var unwrapped = ProtoWire.Unwrap(schema);
        return (IProtoMessageWriter<T>)unwrapped.Accept(new Visitor());
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
            var visitor = new ProtoMemberWriterCompiler<T>();
            schema.VisitMembers(visitor);
            return new StructureProtoMessageWriter<T>(visitor.Writers);
        }

        public object VisitUnion<T>(IUnionSchema<T> schema)
        {
            var visitor = new ProtoUnionCaseWriterCompiler<T>();
            schema.VisitCases(visitor);
            return new UnionProtoMessageWriter<T>(visitor.Writers);
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

    private static StructureProtoMessageWriter<TWire> CompileUnwrapped<TWire, TBuilder>(
        StructSchema<TWire, TBuilder> schema
    )
    {
        var visitor = new ProtoMemberWriterCompiler<TWire>();
        schema.VisitMembers(visitor);
        return new StructureProtoMessageWriter<TWire>(visitor.Writers);
    }

    private static StructureProtoMessageWriter<SmithyUnit> CompileUnwrapped(UnitSchema schema) =>
        new([]);

    private static UnionProtoMessageWriter<TWire> CompileUnwrapped<TWire>(UnionSchema<TWire> schema)
    {
        var visitor = new ProtoUnionCaseWriterCompiler<TWire>();
        schema.VisitCases(visitor);
        return new UnionProtoMessageWriter<TWire>(visitor.Writers);
    }

    private static IProtoMessageWriter<T> CompileUnwrapped<T>(Schema schema) =>
        throw new InvalidOperationException(
            "Protobuf messages must be backed by a structure or union schema."
        );
}

internal sealed class ProtoMemberWriterCompiler<TContainer> : IMemberVisitor<TContainer>
{
    private readonly List<IProtoMemberWriter<TContainer>> writers = [];

    public IReadOnlyList<IProtoMemberWriter<TContainer>> Writers => writers;

    public void Visit<TValue>(IMemberSchema<TContainer, TValue> member) =>
        writers.Add(new ProtoMemberWriter<TContainer, TValue>(member));
}

internal sealed class ProtoMemberWriter<TContainer, TValue>(
    IMemberSchema<TContainer, TValue> member
) : IProtoMemberWriter<TContainer>
{
    public void Write(ProtoWriter writer, TContainer value)
    {
        var memberValue = member.GetValue(value);
        if (memberValue is null)
        {
            return;
        }

        ProtoWire.WriteField(writer, member, memberValue);
    }
}

internal sealed class StructureProtoMessageWriter<T>(
    IReadOnlyList<IProtoMemberWriter<T>> memberWriters
) : IProtoMessageWriter<T>
{
    public void Write(ProtoWriter writer, T value)
    {
        foreach (var memberWriter in memberWriters)
        {
            memberWriter.Write(writer, value);
        }
    }
}

internal sealed class ProtoUnionCaseWriterCompiler<TUnion> : IUnionCaseVisitor<TUnion>
{
    private readonly List<IProtoUnionCaseWriter<TUnion>> writers = [];

    public IReadOnlyList<IProtoUnionCaseWriter<TUnion>> Writers => writers;

    public void Visit<TValue>(IUnionCaseSchema<TUnion, TValue> unionCase) =>
        writers.Add(new ProtoUnionCaseWriter<TUnion, TValue>(unionCase));
}

internal sealed class ProtoUnionCaseWriter<TUnion, TValue>(
    IUnionCaseSchema<TUnion, TValue> unionCase
) : IProtoUnionCaseWriter<TUnion>
{
    public bool TryWrite(ProtoWriter writer, TUnion value)
    {
        if (!unionCase.Matches(value))
        {
            return false;
        }

        ProtoWire.WriteTagged(
            writer,
            ProtoWire.ProtoIndex(unionCase.Traits),
            unionCase.TargetSchema,
            unionCase.Traits,
            unionCase.GetValue(value)!
        );
        return true;
    }
}

internal sealed class UnionProtoMessageWriter<T>(
    IReadOnlyList<IProtoUnionCaseWriter<T>> caseWriters
) : IProtoMessageWriter<T>
{
    public void Write(ProtoWriter writer, T value)
    {
        foreach (var caseWriter in caseWriters)
        {
            if (caseWriter.TryWrite(writer, value))
            {
                return;
            }
        }

        throw new InvalidOperationException($"No union case matched '{typeof(T).Name}'.");
    }
}
