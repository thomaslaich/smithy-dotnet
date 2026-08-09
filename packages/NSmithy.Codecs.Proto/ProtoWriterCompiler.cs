using System.Globalization;
using System.Numerics;
using System.Text;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Proto;

internal interface IProtoMessageWriter<in T>
{
    void Write(ProtoWriter writer, T value);
}

internal interface IProtoValueWriter<in T>
{
    WireType WireType { get; }

    void WriteBody(ProtoWriter writer, T value);
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
        return new ProtoValueWriterCompiler().CompileMessage(schema);
    }
}

internal sealed class ProtoValueWriterCompiler : ISchemaVisitor<object>
{
    private static readonly IReadOnlyDictionary<ShapeId, Trait> EmptyTraits =
        new Dictionary<ShapeId, Trait>();

    private readonly Dictionary<Schema, object> cache = new(ReferenceEqualityComparer.Instance);

    public IProtoMessageWriter<T> CompileMessage<T>(Schema<T> schema)
    {
        var unwrapped = ProtoWire.Unwrap(schema);
        return (IProtoMessageWriter<T>)unwrapped.Accept(new MessageWriterVisitor(this));
    }

    public IProtoValueWriter<T> CompileValue<T>(Schema<T> schema) =>
        CompileValue(schema, EmptyTraits);

    public IProtoValueWriter<T> CompileValue<T>(
        Schema<T> schema,
        IReadOnlyDictionary<ShapeId, Trait> traits
    )
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(traits);

        var resolved = schema.Resolved;
        if (traits.Count != 0)
        {
            return (IProtoValueWriter<T>)
                resolved.Accept(new MemberTraitProtoWriterCompiler(this, traits));
        }

        if (cache.TryGetValue(resolved, out var cached))
        {
            return (IProtoValueWriter<T>)cached;
        }

        var deferred = new DeferredProtoValueWriter<T>();
        cache.Add(resolved, deferred);
        deferred.Set((IProtoValueWriter<T>)resolved.Accept(this));
        return deferred;
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

    public object VisitDocument(Schema<Document> schema) => new DocumentProtoValueWriter();

    public object VisitNullable<T>(NullableSchema<T> schema)
        where T : struct => new NullableProtoValueWriter<T>(CompileValue(schema.TargetSchema));

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
        var visitor = new ProtoMemberWriterCompiler<T>(this);
        schema.VisitMembers(visitor);
        return new MessageProtoValueWriter<T>(new StructureProtoMessageWriter<T>(visitor.Writers));
    }

    public object VisitUnion<T>(IUnionSchema<T> schema)
    {
        var visitor = new ProtoUnionCaseWriterCompiler<T>(this);
        schema.VisitCases(visitor);
        return new MessageProtoValueWriter<T>(new UnionProtoMessageWriter<T>(visitor.Writers));
    }

    public object VisitStringEnum<T>(StringEnumSchema<T> schema)
        where T : IStringEnumValue<T> => Scalar(schema, EmptyTraits);

    public object VisitIntEnum<T>(IntEnumSchema<T> schema)
        where T : struct, Enum => Scalar(schema, EmptyTraits);

    private static ScalarProtoValueWriter<T> Scalar<T>(
        Schema<T> schema,
        IReadOnlyDictionary<ShapeId, Trait> traits
    ) => new(schema, traits);

    private static object UnsupportedAggregateValue() =>
        throw new NotSupportedException(
            "Proto codec cannot encode list or map schemas as standalone protobuf values."
        );
}

internal sealed class MessageWriterVisitor(ProtoValueWriterCompiler compiler)
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

    public object VisitEventStream<TEvent>(EventStreamSchema<TEvent> schema) => Unsupported();

    public object VisitList<TCollection, TElement, TBuilder>(
        IListSchema<TCollection, TElement, TBuilder> schema
    ) => Unsupported();

    public object VisitMap<TDictionary, TValue, TBuilder>(
        IMapSchema<TDictionary, TValue, TBuilder> schema
    ) => Unsupported();

    public object VisitStruct<T, TBuilder>(IStructSchema<T, TBuilder> schema)
    {
        var visitor = new ProtoMemberWriterCompiler<T>(compiler);
        schema.VisitMembers(visitor);
        return new StructureProtoMessageWriter<T>(visitor.Writers);
    }

    public object VisitUnion<T>(IUnionSchema<T> schema)
    {
        var visitor = new ProtoUnionCaseWriterCompiler<T>(compiler);
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

internal sealed class MemberTraitProtoWriterCompiler(
    ProtoValueWriterCompiler inner,
    IReadOnlyDictionary<ShapeId, Trait> traits
) : ISchemaVisitor<object>
{
    public object VisitBoolean(Schema<bool> schema) =>
        new ScalarProtoValueWriter<bool>(schema, traits);

    public object VisitByte(Schema<sbyte> schema) =>
        new ScalarProtoValueWriter<sbyte>(schema, traits);

    public object VisitShort(Schema<short> schema) =>
        new ScalarProtoValueWriter<short>(schema, traits);

    public object VisitInteger(Schema<int> schema) =>
        new ScalarProtoValueWriter<int>(schema, traits);

    public object VisitLong(Schema<long> schema) =>
        new ScalarProtoValueWriter<long>(schema, traits);

    public object VisitFloat(Schema<float> schema) =>
        new ScalarProtoValueWriter<float>(schema, traits);

    public object VisitDouble(Schema<double> schema) =>
        new ScalarProtoValueWriter<double>(schema, traits);

    public object VisitBigInteger(Schema<BigInteger> schema) =>
        new ScalarProtoValueWriter<BigInteger>(schema, traits);

    public object VisitBigDecimal(Schema<decimal> schema) =>
        new ScalarProtoValueWriter<decimal>(schema, traits);

    public object VisitString(Schema<string> schema) =>
        new ScalarProtoValueWriter<string>(schema, traits);

    public object VisitBlob(Schema<byte[]> schema) =>
        new ScalarProtoValueWriter<byte[]>(schema, traits);

    public object VisitTimestamp(Schema<DateTimeOffset> schema) =>
        new ScalarProtoValueWriter<DateTimeOffset>(schema, traits);

    public object VisitDocument(Schema<Document> schema) => inner.CompileValue(schema);

    public object VisitNullable<T>(NullableSchema<T> schema)
        where T : struct =>
        new NullableProtoValueWriter<T>(inner.CompileValue(schema.TargetSchema, traits));

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
        where T : IStringEnumValue<T> => new ScalarProtoValueWriter<T>(schema, traits);

    public object VisitIntEnum<T>(IntEnumSchema<T> schema)
        where T : struct, Enum => new ScalarProtoValueWriter<T>(schema, traits);
}

internal sealed class DeferredProtoValueWriter<T> : IProtoValueWriter<T>
{
    private IProtoValueWriter<T>? inner;

    public WireType WireType =>
        inner?.WireType
        ?? throw new InvalidOperationException("Proto writer has not been initialized.");

    public void Set(IProtoValueWriter<T> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        inner = writer;
    }

    public void WriteBody(ProtoWriter writer, T value)
    {
        if (inner is null)
        {
            throw new InvalidOperationException("Proto writer has not been initialized.");
        }

        inner.WriteBody(writer, value);
    }
}

internal sealed class ScalarProtoValueWriter<T>(
    Schema<T> schema,
    IReadOnlyDictionary<ShapeId, Trait> traits
) : IProtoValueWriter<T>
{
    public WireType WireType { get; } = ProtoWire.WireTypeOf(ProtoWire.Unwrap(schema).Kind, traits);

    public void WriteBody(ProtoWriter writer, T value)
    {
        var resolved = ProtoWire.Unwrap(schema);
        switch (resolved.Kind)
        {
            case ShapeKind.Boolean:
                writer.WriteVarint((bool)(object)value! ? 1UL : 0UL);
                break;
            case ShapeKind.Byte:
                ProtoWire.WriteInteger(writer, resolved.Kind, traits, (sbyte)(object)value!);
                break;
            case ShapeKind.Short:
                ProtoWire.WriteInteger(writer, resolved.Kind, traits, (short)(object)value!);
                break;
            case ShapeKind.Integer:
                ProtoWire.WriteInteger(writer, resolved.Kind, traits, (int)(object)value!);
                break;
            case ShapeKind.Long:
                ProtoWire.WriteInteger(writer, resolved.Kind, traits, (long)(object)value!);
                break;
            case ShapeKind.Float:
                writer.WriteFixed32(BitConverter.SingleToUInt32Bits((float)(object)value!));
                break;
            case ShapeKind.Double:
                writer.WriteFixed64(BitConverter.DoubleToUInt64Bits((double)(object)value!));
                break;
            case ShapeKind.String:
                writer.WriteLengthDelimited(Encoding.UTF8.GetBytes((string)(object)value!));
                break;
            case ShapeKind.Blob:
                writer.WriteLengthDelimited((byte[])(object)value!);
                break;
            case ShapeKind.BigInteger:
                writer.WriteLengthDelimited(
                    Encoding.UTF8.GetBytes(
                        ((BigInteger)(object)value!).ToString(CultureInfo.InvariantCulture)
                    )
                );
                break;
            case ShapeKind.BigDecimal:
                writer.WriteLengthDelimited(
                    Encoding.UTF8.GetBytes(
                        ((decimal)(object)value!).ToString(CultureInfo.InvariantCulture)
                    )
                );
                break;
            case ShapeKind.IntEnum:
                writer.WriteVarint(
                    (ulong)(long)((IIntEnumSchema)resolved).GetIntegerValueObject(value!)
                );
                break;
            case ShapeKind.Timestamp:
                writer.WriteLengthDelimited(
                    ProtoWire.EncodeTimestamp((DateTimeOffset)(object)value!)
                );
                break;
            case ShapeKind.Enum:
                writer.WriteVarint(
                    (ulong)
                        (long)
                            ProtoWire.EnumOrdinal(
                                resolved,
                                ((IStringEnumValue)(object)value!).Value
                            )
                );
                break;
            default:
                throw new NotSupportedException(
                    $"Proto codec cannot encode schema kind '{resolved.Kind}'."
                );
        }
    }
}

internal sealed class NullableProtoValueWriter<T>(IProtoValueWriter<T> inner)
    : IProtoValueWriter<T?>
    where T : struct
{
    public WireType WireType => inner.WireType;

    public void WriteBody(ProtoWriter writer, T? value)
    {
        if (value.HasValue)
        {
            inner.WriteBody(writer, value.Value);
        }
    }
}

internal sealed class MessageProtoValueWriter<T>(IProtoMessageWriter<T> messageWriter)
    : IProtoValueWriter<T>
{
    public WireType WireType => WireType.Len;

    public void WriteBody(ProtoWriter writer, T value)
    {
        var sub = new ProtoWriter();
        messageWriter.Write(sub, value);
        writer.WriteLengthDelimited(sub.ToArray());
    }
}

internal sealed class DocumentProtoValueWriter : IProtoValueWriter<Document>
{
    public WireType WireType => WireType.Len;

    public void WriteBody(ProtoWriter writer, Document value)
    {
        var sub = new ProtoWriter();
        ProtoWire.EncodeDocumentValue(sub, value);
        writer.WriteLengthDelimited(sub.ToArray());
    }
}

internal sealed class ProtoMemberWriterCompiler<TContainer>(ProtoValueWriterCompiler compiler)
    : IMemberVisitor<TContainer>
{
    private readonly List<IProtoMemberWriter<TContainer>> writers = [];

    public IReadOnlyList<IProtoMemberWriter<TContainer>> Writers => writers;

    public void Visit<TValue>(IMemberSchema<TContainer, TValue> member)
    {
        var target = ProtoWire.Unwrap(member.TargetSchema);
        if (target is IUnionSchema union && ProtoWire.IsInlinedUnion(target))
        {
            AddInlinedOneOfWriter(member, (dynamic)union);
            return;
        }

        writers.Add(
            target switch
            {
                IListSchema list => CreateListWriter(member, (dynamic)list),
                IMapSchema map => CreateMapWriter(member, (dynamic)map),
                _ => new ValueProtoMemberWriter<TContainer, TValue>(
                    member,
                    compiler.CompileValue(member.TargetSchema, member.MemberTraits)
                ),
            }
        );
    }

    private void AddInlinedOneOfWriter<TUnion>(
        IMemberSchema<TContainer, TUnion> member,
        IUnionSchema<TUnion> union
    )
    {
        var visitor = new InlinedProtoUnionMemberWriterCompiler<TContainer, TUnion>(compiler);
        union.VisitCases(visitor);
        writers.Add(new InlinedProtoUnionMemberWriter<TContainer, TUnion>(member, visitor.Writers));
    }

    private ListProtoMemberWriter<TContainer, TValue, TElement> CreateListWriter<TValue, TElement>(
        IMemberSchema<TContainer, TValue> member,
        IListSchema<TValue, TElement> list
    ) =>
        new(
            member,
            list,
            compiler.CompileValue(
                list.TypedElementMember.TargetSchema,
                list.TypedElementMember.MemberTraits
            )
        );

    private MapProtoMemberWriter<TContainer, TValue, TMapValue> CreateMapWriter<TValue, TMapValue>(
        IMemberSchema<TContainer, TValue> member,
        IMapSchema<TValue, TMapValue> map
    ) =>
        new(
            member,
            map,
            ProtoWire.IsSparse((Schema)map),
            compiler.CompileValue(
                map.TypedValueMember.TargetSchema,
                map.TypedValueMember.MemberTraits
            )
        );
}

internal sealed class ValueProtoMemberWriter<TContainer, TValue>(
    IMemberSchema<TContainer, TValue> member,
    IProtoValueWriter<TValue> valueWriter
) : IProtoMemberWriter<TContainer>
{
    private readonly int fieldNumber = ProtoWire.ProtoIndex(member.MemberTraits);

    public void Write(ProtoWriter writer, TContainer value)
    {
        var memberValue = member.GetValue(value);
        if (memberValue is null)
        {
            return;
        }

        writer.WriteTag(fieldNumber, valueWriter.WireType);
        valueWriter.WriteBody(writer, memberValue);
    }
}

internal sealed class ListProtoMemberWriter<TContainer, TCollection, TElement>(
    IMemberSchema<TContainer, TCollection> member,
    IListSchema<TCollection, TElement> list,
    IProtoValueWriter<TElement> elementWriter
) : IProtoMemberWriter<TContainer>
{
    private readonly int fieldNumber = ProtoWire.ProtoIndex(member.MemberTraits);
    private readonly bool packable = ProtoWire.IsPackableScalar(
        ProtoWire.Unwrap(list.ElementSchema).Kind
    );

    public void Write(ProtoWriter writer, TContainer value)
    {
        var memberValue = member.GetValue(value);
        if (memberValue is null)
        {
            return;
        }

        if (packable)
        {
            var packed = new ProtoWriter();
            foreach (var item in list.GetElements(memberValue))
            {
                elementWriter.WriteBody(packed, item);
            }

            if (packed.Length > 0)
            {
                writer.WriteTag(fieldNumber, WireType.Len);
                writer.WriteLengthDelimited(packed.ToArray());
            }

            return;
        }

        foreach (var item in list.GetElements(memberValue))
        {
            writer.WriteTag(fieldNumber, elementWriter.WireType);
            elementWriter.WriteBody(writer, item);
        }
    }
}

internal sealed class MapProtoMemberWriter<TContainer, TDictionary, TValue>(
    IMemberSchema<TContainer, TDictionary> member,
    IMapSchema<TDictionary, TValue> map,
    bool sparse,
    IProtoValueWriter<TValue> valueWriter
) : IProtoMemberWriter<TContainer>
{
    private readonly int fieldNumber = ProtoWire.ProtoIndex(member.MemberTraits);
    private readonly SparseScalarValueWriter<TValue> sparseWriter = new(
        map.TypedValueMember.TargetSchema
    );

    public void Write(ProtoWriter writer, TContainer value)
    {
        var memberValue = member.GetValue(value);
        if (memberValue is null)
        {
            return;
        }

        foreach (var entry in map.GetEntries(memberValue))
        {
            var sub = new ProtoWriter();
            sub.WriteTag(1, WireType.Len);
            sub.WriteLengthDelimited(Encoding.UTF8.GetBytes(entry.Key));

            if (sparse)
            {
                sub.WriteTag(2, WireType.Len);
                sparseWriter.WriteBody(sub, entry.Value);
            }
            else if (entry.Value is not null)
            {
                sub.WriteTag(2, valueWriter.WireType);
                valueWriter.WriteBody(sub, entry.Value);
            }

            writer.WriteTag(fieldNumber, WireType.Len);
            writer.WriteLengthDelimited(sub.ToArray());
        }
    }
}

internal sealed class SparseScalarValueWriter<T>(Schema<T> schema)
{
    public void WriteBody(ProtoWriter writer, T? value)
    {
        var sub = new ProtoWriter();
        ProtoWire.EncodeScalarValueMessage(sub, schema, value);
        writer.WriteLengthDelimited(sub.ToArray());
    }
}

internal sealed class InlinedProtoUnionMemberWriterCompiler<TContainer, TUnion>(
    ProtoValueWriterCompiler compiler
) : IUnionCaseVisitor<TUnion>
{
    private readonly List<IProtoUnionCaseWriter<TUnion>> writers = [];

    public IReadOnlyList<IProtoUnionCaseWriter<TUnion>> Writers => writers;

    public void Visit<TValue>(IUnionCaseSchema<TUnion, TValue> unionCase) =>
        writers.Add(
            new ProtoUnionCaseWriter<TUnion, TValue>(
                unionCase,
                compiler.CompileValue(unionCase.TargetSchema, unionCase.Traits)
            )
        );
}

internal sealed class InlinedProtoUnionMemberWriter<TContainer, TUnion>(
    IMemberSchema<TContainer, TUnion> member,
    IReadOnlyList<IProtoUnionCaseWriter<TUnion>> caseWriters
) : IProtoMemberWriter<TContainer>
{
    public void Write(ProtoWriter writer, TContainer value)
    {
        var unionValue = member.GetValue(value);
        if (unionValue is null)
        {
            return;
        }

        foreach (var caseWriter in caseWriters)
        {
            if (caseWriter.TryWrite(writer, unionValue))
            {
                return;
            }
        }

        throw new InvalidOperationException($"No union case matched '{typeof(TUnion).Name}'.");
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

internal sealed class ProtoUnionCaseWriterCompiler<TUnion>(ProtoValueWriterCompiler compiler)
    : IUnionCaseVisitor<TUnion>
{
    private readonly List<IProtoUnionCaseWriter<TUnion>> writers = [];

    public IReadOnlyList<IProtoUnionCaseWriter<TUnion>> Writers => writers;

    public void Visit<TValue>(IUnionCaseSchema<TUnion, TValue> unionCase) =>
        writers.Add(
            new ProtoUnionCaseWriter<TUnion, TValue>(
                unionCase,
                compiler.CompileValue(unionCase.TargetSchema, unionCase.Traits)
            )
        );
}

internal sealed class ProtoUnionCaseWriter<TUnion, TValue>(
    IUnionCaseSchema<TUnion, TValue> unionCase,
    IProtoValueWriter<TValue> valueWriter
) : IProtoUnionCaseWriter<TUnion>
{
    private readonly int fieldNumber = ProtoWire.ProtoIndex(unionCase.Traits);

    public bool TryWrite(ProtoWriter writer, TUnion value)
    {
        if (!unionCase.Matches(value))
        {
            return false;
        }

        writer.WriteTag(fieldNumber, valueWriter.WireType);
        valueWriter.WriteBody(writer, unionCase.GetValue(value)!);
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
