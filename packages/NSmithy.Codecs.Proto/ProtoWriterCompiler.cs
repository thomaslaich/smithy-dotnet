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

internal interface IProtoMemberPlan<in TValue>
{
    void Write(ProtoWriter writer, TValue value);
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

    private readonly SchemaCompilationCache cache = new();

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

        return cache.GetOrCompile<IProtoValueWriter<T>, DeferredProtoValueWriter<T>>(
            resolved,
            static () => new DeferredProtoValueWriter<T>(),
            target => (IProtoValueWriter<T>)target.Accept(this)
        );
    }

    public object VisitBoolean(Schema<bool> schema) => new BooleanProtoValueWriter();

    public object VisitByte(Schema<sbyte> schema) =>
        new ByteProtoValueWriter(ShapeKind.Byte, EmptyTraits);

    public object VisitShort(Schema<short> schema) =>
        new ShortProtoValueWriter(ShapeKind.Short, EmptyTraits);

    public object VisitInteger(Schema<int> schema) =>
        new IntegerProtoValueWriter(ShapeKind.Integer, EmptyTraits);

    public object VisitLong(Schema<long> schema) =>
        new LongProtoValueWriter(ShapeKind.Long, EmptyTraits);

    public object VisitFloat(Schema<float> schema) => new FloatProtoValueWriter();

    public object VisitDouble(Schema<double> schema) => new DoubleProtoValueWriter();

    public object VisitBigInteger(Schema<BigInteger> schema) => Scalar(schema, EmptyTraits);

    public object VisitBigDecimal(Schema<decimal> schema) => Scalar(schema, EmptyTraits);

    public object VisitString(Schema<string> schema) => new StringProtoValueWriter();

    public object VisitBlob(Schema<byte[]> schema) => new BlobProtoValueWriter();

    public object VisitTimestamp(Schema<DateTimeOffset> schema) => Scalar(schema, EmptyTraits);

    public object VisitDocument(Schema<Document> schema) => new DocumentProtoValueWriter();

    public object VisitNullable<T>(NullableSchema<T> schema)
        where T : struct => new NullableProtoValueWriter<T>(CompileValue(schema.TargetSchema));

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
        var visitor = new ProtoMemberWriterCompiler<T>(this);
        schema.VisitMembers(visitor);
        return new MessageProtoValueWriter<T>(CreateStructureWriter(schema, visitor));
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

    internal static IProtoMessageWriter<T> CreateStructureWriter<T>(
        IStructSchema<T> schema,
        ProtoMemberWriterCompiler<T> compiler
    ) =>
        schema.ValueSerializer is { } valueSerializer
            ? new DirectStructureProtoMessageWriter<T>(valueSerializer, compiler.Plans)
            : new FallbackStructureProtoMessageWriter<T>(compiler.Writers);
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
        var visitor = new ProtoMemberWriterCompiler<T>(compiler);
        schema.VisitMembers(visitor);
        return ProtoValueWriterCompiler.CreateStructureWriter(schema, visitor);
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
    public object VisitBoolean(Schema<bool> schema) => new BooleanProtoValueWriter();

    public object VisitByte(Schema<sbyte> schema) =>
        new ByteProtoValueWriter(ShapeKind.Byte, traits);

    public object VisitShort(Schema<short> schema) =>
        new ShortProtoValueWriter(ShapeKind.Short, traits);

    public object VisitInteger(Schema<int> schema) =>
        new IntegerProtoValueWriter(ShapeKind.Integer, traits);

    public object VisitLong(Schema<long> schema) =>
        new LongProtoValueWriter(ShapeKind.Long, traits);

    public object VisitFloat(Schema<float> schema) => new FloatProtoValueWriter();

    public object VisitDouble(Schema<double> schema) => new DoubleProtoValueWriter();

    public object VisitBigInteger(Schema<BigInteger> schema) =>
        new ScalarProtoValueWriter<BigInteger>(schema, traits);

    public object VisitBigDecimal(Schema<decimal> schema) =>
        new ScalarProtoValueWriter<decimal>(schema, traits);

    public object VisitString(Schema<string> schema) => new StringProtoValueWriter();

    public object VisitBlob(Schema<byte[]> schema) => new BlobProtoValueWriter();

    public object VisitTimestamp(Schema<DateTimeOffset> schema) =>
        new ScalarProtoValueWriter<DateTimeOffset>(schema, traits);

    public object VisitDocument(Schema<Document> schema) => inner.CompileValue(schema);

    public object VisitNullable<T>(NullableSchema<T> schema)
        where T : struct =>
        new NullableProtoValueWriter<T>(inner.CompileValue(schema.TargetSchema, traits));

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
        where T : IStringEnumValue<T> => new ScalarProtoValueWriter<T>(schema, traits);

    public object VisitIntEnum<T>(IntEnumSchema<T> schema)
        where T : struct, Enum => new ScalarProtoValueWriter<T>(schema, traits);
}

internal sealed class DeferredProtoValueWriter<T>
    : IProtoValueWriter<T>,
        IDeferredCompilation<IProtoValueWriter<T>>
{
    public void Complete(IProtoValueWriter<T> compiled) => Set(compiled);

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

internal sealed class BooleanProtoValueWriter : IProtoValueWriter<bool>
{
    public WireType WireType => WireType.Varint;

    public void WriteBody(ProtoWriter writer, bool value) => writer.WriteVarint(value ? 1UL : 0UL);
}

internal sealed class ByteProtoValueWriter(
    ShapeKind kind,
    IReadOnlyDictionary<ShapeId, Trait> traits
) : IProtoValueWriter<sbyte>
{
    private readonly ProtoWire.IntEncoding encoding = ProtoWire.IntEncodingOf(kind, traits);

    public WireType WireType { get; } = ProtoWire.WireTypeOf(kind, traits);

    public void WriteBody(ProtoWriter writer, sbyte value) =>
        ProtoWire.WriteInteger(writer, encoding, value);
}

internal sealed class ShortProtoValueWriter(
    ShapeKind kind,
    IReadOnlyDictionary<ShapeId, Trait> traits
) : IProtoValueWriter<short>
{
    private readonly ProtoWire.IntEncoding encoding = ProtoWire.IntEncodingOf(kind, traits);

    public WireType WireType { get; } = ProtoWire.WireTypeOf(kind, traits);

    public void WriteBody(ProtoWriter writer, short value) =>
        ProtoWire.WriteInteger(writer, encoding, value);
}

internal sealed class IntegerProtoValueWriter(
    ShapeKind kind,
    IReadOnlyDictionary<ShapeId, Trait> traits
) : IProtoValueWriter<int>
{
    private readonly ProtoWire.IntEncoding encoding = ProtoWire.IntEncodingOf(kind, traits);

    public WireType WireType { get; } = ProtoWire.WireTypeOf(kind, traits);

    public void WriteBody(ProtoWriter writer, int value) =>
        ProtoWire.WriteInteger(writer, encoding, value);
}

internal sealed class LongProtoValueWriter(
    ShapeKind kind,
    IReadOnlyDictionary<ShapeId, Trait> traits
) : IProtoValueWriter<long>
{
    private readonly ProtoWire.IntEncoding encoding = ProtoWire.IntEncodingOf(kind, traits);

    public WireType WireType { get; } = ProtoWire.WireTypeOf(kind, traits);

    public void WriteBody(ProtoWriter writer, long value) =>
        ProtoWire.WriteInteger(writer, encoding, value);
}

internal sealed class FloatProtoValueWriter : IProtoValueWriter<float>
{
    public WireType WireType => WireType.I32;

    public void WriteBody(ProtoWriter writer, float value) =>
        writer.WriteFixed32(BitConverter.SingleToUInt32Bits(value));
}

internal sealed class DoubleProtoValueWriter : IProtoValueWriter<double>
{
    public WireType WireType => WireType.I64;

    public void WriteBody(ProtoWriter writer, double value) =>
        writer.WriteFixed64(BitConverter.DoubleToUInt64Bits(value));
}

internal sealed class StringProtoValueWriter : IProtoValueWriter<string>
{
    public WireType WireType => WireType.Len;

    public void WriteBody(ProtoWriter writer, string value) =>
        writer.WriteLengthDelimitedUtf8(value);
}

internal sealed class BlobProtoValueWriter : IProtoValueWriter<byte[]>
{
    public WireType WireType => WireType.Len;

    public void WriteBody(ProtoWriter writer, byte[] value) => writer.WriteLengthDelimited(value);
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
                writer.WriteLengthDelimitedUtf8((string)(object)value!);
                break;
            case ShapeKind.Blob:
                writer.WriteLengthDelimited((byte[])(object)value!);
                break;
            case ShapeKind.BigInteger:
                writer.WriteLengthDelimitedUtf8(
                    ((BigInteger)(object)value!).ToString(CultureInfo.InvariantCulture)
                );
                break;
            case ShapeKind.BigDecimal:
                writer.WriteLengthDelimitedUtf8(
                    ((decimal)(object)value!).ToString(CultureInfo.InvariantCulture)
                );
                break;
            case ShapeKind.IntEnum:
                writer.WriteVarint(
                    (ulong)(long)((IIntEnumSchema)resolved).GetIntegerValueObject(value!)
                );
                break;
            case ShapeKind.Timestamp:
                var timestamp = writer.BeginLengthDelimited();
                ProtoWire.EncodeTimestamp(writer, (DateTimeOffset)(object)value!);
                writer.EndLengthDelimited(timestamp);
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
        var prefix = writer.BeginLengthDelimited();
        messageWriter.Write(writer, value);
        writer.EndLengthDelimited(prefix);
    }
}

internal sealed class DocumentProtoValueWriter : IProtoValueWriter<Document>
{
    public WireType WireType => WireType.Len;

    public void WriteBody(ProtoWriter writer, Document value)
    {
        var prefix = writer.BeginLengthDelimited();
        ProtoWire.EncodeDocumentValue(writer, value);
        writer.EndLengthDelimited(prefix);
    }
}

internal sealed class ProtoMemberWriterCompiler<TContainer>(ProtoValueWriterCompiler compiler)
    : IMemberVisitor<TContainer>
{
    private readonly List<IProtoMemberWriter<TContainer>> writers = [];
    private readonly List<object> plans = [];

    public IProtoMemberWriter<TContainer>[] Writers => [.. writers];

    public object[] Plans => [.. plans];

    public void Visit<TValue>(IMemberSchema<TContainer, TValue> member)
    {
        var target = ProtoWire.Unwrap(member.TargetSchema);
        if (target is IUnionSchema union && ProtoWire.IsInlinedUnion(target))
        {
            AddInlinedOneOfPlan(member, (dynamic)union);
            return;
        }

        var fieldNumber = ProtoWire.FieldNumber(member.Id, member.MemberTraits, writers.Count);
        IProtoMemberPlan<TValue> plan = target switch
        {
            IListSchema list => CreateListPlan((dynamic)list, fieldNumber),
            IMapSchema map => CreateMapPlan((dynamic)map, fieldNumber),
            _ => new ValueProtoMemberPlan<TValue>(
                fieldNumber,
                compiler.CompileValue(member.TargetSchema, member.MemberTraits)
            ),
        };
        plans.Add(plan);
        writers.Add(new ProtoMemberWriter<TContainer, TValue>(member, plan));
    }

    private void AddInlinedOneOfPlan<TUnion>(
        IMemberSchema<TContainer, TUnion> member,
        IUnionSchema<TUnion> union
    )
    {
        var visitor = new InlinedProtoUnionMemberWriterCompiler<TContainer, TUnion>(compiler);
        union.VisitCases(visitor);
        var plan = new InlinedProtoUnionMemberPlan<TUnion>(visitor.Writers);
        plans.Add(plan);
        writers.Add(new ProtoMemberWriter<TContainer, TUnion>(member, plan));
    }

    private ListProtoMemberPlan<TValue, TElement> CreateListPlan<TValue, TElement>(
        IListSchema<TValue, TElement> list,
        int fieldNumber
    ) =>
        new(
            list,
            fieldNumber,
            compiler.CompileValue(
                list.TypedElementMember.TargetSchema,
                list.TypedElementMember.MemberTraits
            )
        );

    private MapProtoMemberPlan<TValue, TMapValue> CreateMapPlan<TValue, TMapValue>(
        IMapSchema<TValue, TMapValue> map,
        int fieldNumber
    ) =>
        new(
            map,
            fieldNumber,
            ProtoWire.IsSparse((Schema)map),
            compiler.CompileValue(
                map.TypedValueMember.TargetSchema,
                map.TypedValueMember.MemberTraits
            )
        );
}

internal sealed class ProtoMemberWriter<TContainer, TValue>(
    IMemberSchema<TContainer, TValue> member,
    IProtoMemberPlan<TValue> plan
) : IProtoMemberWriter<TContainer>
{
    public void Write(ProtoWriter writer, TContainer value) =>
        plan.Write(writer, member.GetValue(value));
}

internal sealed class ValueProtoMemberPlan<TValue>(
    int fieldNumber,
    IProtoValueWriter<TValue> valueWriter
) : IProtoMemberPlan<TValue>
{
    private readonly WireType wireType = valueWriter.WireType;

    public void Write(ProtoWriter writer, TValue memberValue)
    {
        if (memberValue is null)
        {
            return;
        }

        writer.WriteTag(fieldNumber, wireType);
        valueWriter.WriteBody(writer, memberValue);
    }
}

internal sealed class ListProtoMemberPlan<TCollection, TElement>(
    IListSchema<TCollection, TElement> list,
    int fieldNumber,
    IProtoValueWriter<TElement> elementWriter
) : IProtoMemberPlan<TCollection>
{
    private readonly bool packable = ProtoWire.IsPackableScalar(
        ProtoWire.Unwrap(list.ElementSchema).Kind
    );
    private readonly WireType elementWireType = elementWriter.WireType;

    public void Write(ProtoWriter writer, TCollection memberValue)
    {
        if (memberValue is null)
        {
            return;
        }

        if (packable)
        {
            var fieldOffset = writer.Length;
            writer.WriteTag(fieldNumber, WireType.Len);
            var prefix = writer.BeginLengthDelimited();
            foreach (var item in list.GetElements(memberValue))
            {
                elementWriter.WriteBody(writer, item);
            }

            if (writer.Length == prefix + 1)
            {
                writer.Rewind(fieldOffset);
            }
            else
            {
                writer.EndLengthDelimited(prefix);
            }

            return;
        }

        foreach (var item in list.GetElements(memberValue))
        {
            writer.WriteTag(fieldNumber, elementWireType);
            elementWriter.WriteBody(writer, item);
        }
    }
}

internal sealed class MapProtoMemberPlan<TDictionary, TValue>(
    IMapSchema<TDictionary, TValue> map,
    int fieldNumber,
    bool sparse,
    IProtoValueWriter<TValue> valueWriter
) : IProtoMemberPlan<TDictionary>
{
    private readonly SparseScalarValueWriter<TValue> sparseWriter = new(
        map.TypedValueMember.TargetSchema
    );
    private readonly WireType valueWireType = valueWriter.WireType;

    public void Write(ProtoWriter writer, TDictionary memberValue)
    {
        if (memberValue is null)
        {
            return;
        }

        foreach (var entry in map.GetEntries(memberValue))
        {
            writer.WriteTag(fieldNumber, WireType.Len);
            var entryPrefix = writer.BeginLengthDelimited();
            writer.WriteTag(1, WireType.Len);
            writer.WriteLengthDelimitedUtf8(entry.Key);

            if (sparse)
            {
                writer.WriteTag(2, WireType.Len);
                sparseWriter.WriteBody(writer, entry.Value);
            }
            else if (entry.Value is not null)
            {
                writer.WriteTag(2, valueWireType);
                valueWriter.WriteBody(writer, entry.Value);
            }

            writer.EndLengthDelimited(entryPrefix);
        }
    }
}

internal sealed class SparseScalarValueWriter<T>(Schema<T> schema)
{
    public void WriteBody(ProtoWriter writer, T? value)
    {
        var prefix = writer.BeginLengthDelimited();
        ProtoWire.EncodeScalarValueMessage(writer, schema, value);
        writer.EndLengthDelimited(prefix);
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
                ProtoWire.FieldNumber(unionCase.Id, unionCase.Traits, writers.Count),
                compiler.CompileValue(unionCase.TargetSchema, unionCase.Traits)
            )
        );
}

internal sealed class InlinedProtoUnionMemberPlan<TUnion>(
    IReadOnlyList<IProtoUnionCaseWriter<TUnion>> caseWriters
) : IProtoMemberPlan<TUnion>
{
    public void Write(ProtoWriter writer, TUnion unionValue)
    {
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

internal readonly struct ProtoStructMemberWriter(ProtoWriter writer, object[] memberPlans)
    : IStructMemberWriter
{
    public void WriteMember<TValue>(int index, TValue value) =>
        ((IProtoMemberPlan<TValue>)memberPlans[index]).Write(writer, value);
}

internal sealed class DirectStructureProtoMessageWriter<T>(
    IStructValueSerializer<T> valueSerializer,
    object[] memberPlans
) : IProtoMessageWriter<T>
{
    public void Write(ProtoWriter writer, T value)
    {
        var memberWriter = new ProtoStructMemberWriter(writer, memberPlans);
        valueSerializer.WriteMembers(value, ref memberWriter);
    }
}

internal sealed class FallbackStructureProtoMessageWriter<T>(IProtoMemberWriter<T>[] memberWriters)
    : IProtoMessageWriter<T>
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
                ProtoWire.FieldNumber(unionCase.Id, unionCase.Traits, writers.Count),
                compiler.CompileValue(unionCase.TargetSchema, unionCase.Traits)
            )
        );
}

internal sealed class ProtoUnionCaseWriter<TUnion, TValue>(
    IUnionCaseSchema<TUnion, TValue> unionCase,
    int fieldNumber,
    IProtoValueWriter<TValue> valueWriter
) : IProtoUnionCaseWriter<TUnion>
{
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
