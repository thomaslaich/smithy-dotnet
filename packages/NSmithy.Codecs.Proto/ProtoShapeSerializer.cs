using System.Globalization;
using System.Numerics;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Proto;

/// <summary>
/// Writes the fields of one protobuf message, or the items of a repeated field or map. A nested
/// message gets its own serializer, positioned on that message's plan.
/// </summary>
internal struct ProtoShapeSerializer : IShapeSerializer
{
    private enum Context
    {
        /// <summary>The top-level message, written without a tag or length.</summary>
        Root,

        /// <summary>A message's fields, each tagged with its own field number.</summary>
        Message,

        /// <summary>A packed repeated field's elements: bodies only, inside one length.</summary>
        Packed,

        /// <summary>A repeated field's elements, each tagged with the field's number.</summary>
        Repeated,

        /// <summary>A map field's entries, each a key/value message tagged with the field's number.</summary>
        Map,
    }

    private readonly ProtoWriter writer;
    private readonly ProtoShapePlan? container;
    private readonly ProtoMemberPlan? root;
    private readonly Context context;
    private readonly int fieldNumber;

    // The length prefix of the map entry the last key opened, or -1 when no entry is open. An
    // entry is closed by the next key, or by the map once its last entry is written.
    private int entryPrefix = -1;

    public ProtoShapeSerializer(ProtoWriter writer, ProtoMemberPlan root)
    {
        this.writer = writer;
        this.root = root;
        context = Context.Root;
    }

    private ProtoShapeSerializer(
        ProtoWriter writer,
        ProtoShapePlan container,
        Context context,
        int fieldNumber = 0
    )
    {
        this.writer = writer;
        this.container = container;
        this.context = context;
        this.fieldNumber = fieldNumber;
    }

    // A list's elements are its member 0, so they index like any other member.
    private readonly ProtoMemberPlan Entry(int member) =>
        member == MemberIndex.Root ? root! : container!.Members[member];

    /// <summary>Writes the tag that precedes a value of <paramref name="member"/>, if it has one.</summary>
    private readonly void Tag(int member, WireType wireType)
    {
        switch (context)
        {
            case Context.Message:
                writer.WriteTag(container!.Members[member].FieldNumber, wireType);
                break;
            case Context.Repeated:
                writer.WriteTag(fieldNumber, wireType);
                break;
            case Context.Map:
                writer.WriteTag(2, wireType);
                break;
        }
    }

    private void CloseEntry()
    {
        if (entryPrefix >= 0)
        {
            writer.EndLengthDelimited(entryPrefix);
            entryPrefix = -1;
        }
    }

    // A sparse map's values are google.protobuf.Value messages, so null is representable.
    private readonly bool IsSparseValue(int member) =>
        context == Context.Map && member == 1 && container!.Sparse;

    // Generic rather than a callback: a lambda capturing the value would allocate its closure on
    // every call of the enclosing write method, sparse or not.
    private readonly void WriteSparseValue<TValue>(Schema<TValue> schema, TValue? value)
    {
        writer.WriteTag(2, WireType.Len);
        var prefix = writer.BeginLengthDelimited();
        ProtoWire.EncodeScalarValueMessage(writer, schema, value);
        writer.EndLengthDelimited(prefix);
    }

    public readonly bool WritesDefault(int member) => false;

    public readonly void WriteNull(int member)
    {
        if (IsSparseValue(member))
        {
            WriteSparseValue(Schemas.String, null);
            return;
        }

        // Proto has no null: an absent value is an absent field.
    }

    public readonly void WriteBoolean(int member, bool value)
    {
        if (IsSparseValue(member))
        {
            WriteSparseValue(Schemas.Boolean, value);
            return;
        }

        Tag(member, WireType.Varint);
        writer.WriteVarint(value ? 1UL : 0UL);
    }

    public readonly void WriteByte(int member, sbyte value) =>
        WriteInteger(member, value, ShapeKind.Byte);

    public readonly void WriteShort(int member, short value) =>
        WriteInteger(member, value, ShapeKind.Short);

    public readonly void WriteInteger(int member, int value) =>
        WriteInteger(member, value, ShapeKind.Integer);

    public readonly void WriteLong(int member, long value) =>
        WriteInteger(member, value, ShapeKind.Long);

    private readonly void WriteInteger(int member, long value, ShapeKind kind)
    {
        if (IsSparseValue(member))
        {
            WriteSparseValue(Schemas.Double, (double)value);
            return;
        }

        var plan = Entry(member);
        Tag(member, plan.WireType);
        ProtoWire.WriteInteger(writer, plan.Encoding, value);
    }

    public readonly void WriteFloat(int member, float value)
    {
        if (IsSparseValue(member))
        {
            WriteSparseValue(Schemas.Double, (double)value);
            return;
        }

        Tag(member, WireType.I32);
        writer.WriteFixed32(BitConverter.SingleToUInt32Bits(value));
    }

    public readonly void WriteDouble(int member, double value)
    {
        if (IsSparseValue(member))
        {
            WriteSparseValue(Schemas.Double, value);
            return;
        }

        Tag(member, WireType.I64);
        writer.WriteFixed64(BitConverter.DoubleToUInt64Bits(value));
    }

    public void WriteBigInteger(int member, BigInteger value) =>
        WriteUtf8(member, value.ToString(CultureInfo.InvariantCulture));

    public void WriteBigDecimal(int member, decimal value) =>
        WriteUtf8(member, value.ToString(CultureInfo.InvariantCulture));

    public void WriteString(int member, string value)
    {
        // A map's key is member 0: it opens an entry, which holds the key and the value after it.
        if (context == Context.Map && member == 0)
        {
            CloseEntry();
            writer.WriteTag(fieldNumber, WireType.Len);
            entryPrefix = writer.BeginLengthDelimited();
            writer.WriteTag(1, WireType.Len);
            writer.WriteLengthDelimitedUtf8(value);
            return;
        }

        if (IsSparseValue(member))
        {
            WriteSparseValue(Schemas.String, value);
            return;
        }

        WriteUtf8(member, value);
    }

    private void WriteUtf8(int member, string value)
    {
        Tag(member, WireType.Len);
        writer.WriteLengthDelimitedUtf8(value);
    }

    public readonly void WriteBlob(int member, byte[] value)
    {
        Tag(member, WireType.Len);
        writer.WriteLengthDelimited(value);
    }

    public readonly void WriteTimestamp(int member, DateTimeOffset value)
    {
        Tag(member, WireType.Len);
        var prefix = writer.BeginLengthDelimited();
        ProtoWire.EncodeTimestamp(writer, value);
        writer.EndLengthDelimited(prefix);
    }

    public readonly void WriteDocument(int member, Document value)
    {
        Tag(member, WireType.Len);
        var prefix = writer.BeginLengthDelimited();
        ProtoWire.EncodeDocumentValue(writer, value);
        writer.EndLengthDelimited(prefix);
    }

    // A string enum is a proto enum whose ordinals follow the model's declaration order; an
    // unknown value is the proto UNSPECIFIED = 0.
    public readonly void WriteStringEnum(int member, string value)
    {
        var ordinals = Entry(member).EnumOrdinals!;
        Tag(member, WireType.Varint);
        writer.WriteVarint(
            (ulong)(long)(ordinals.TryGetValue(value, out var ordinal) ? ordinal : 0)
        );
    }

    public readonly void WriteIntEnum(int member, int value)
    {
        Tag(member, WireType.Varint);
        writer.WriteVarint((ulong)(long)value);
    }

    public readonly void WriteStream(int member, Stream value) =>
        throw new NotSupportedException("Proto codec does not support streaming blob schemas.");

    public readonly void WriteEventStream<TEvent>(
        int member,
        IAsyncEnumerable<TEvent> events,
        Schema<TEvent> eventSchema
    ) => throw new NotSupportedException("Proto codec does not support event stream schemas.");

    public readonly void WriteStruct<T>(int member, T value, IStructSchema<T> schema)
    {
        var plan = Entry(member);
        if (context == Context.Root)
        {
            var fields = new ProtoShapeSerializer(writer, plan.Shape!, Context.Message);
            schema.SerializeMembers(value, ref fields);
            return;
        }

        Tag(member, WireType.Len);
        var prefix = writer.BeginLengthDelimited();
        var nested = new ProtoShapeSerializer(writer, plan.Shape!, Context.Message);
        schema.SerializeMembers(value, ref nested);
        writer.EndLengthDelimited(prefix);
    }

    public readonly void WriteUnion<T>(int member, T value, UnionSchema<T> schema)
    {
        var plan = Entry(member);
        // The top-level message, or an inlined oneof, is its case's field in this message.
        if (context == Context.Root || (context == Context.Message && plan.IsInlinedUnion))
        {
            var fields = new ProtoShapeSerializer(writer, plan.Shape!, Context.Message);
            schema.SerializeCase(value, ref fields);
            return;
        }

        Tag(member, WireType.Len);
        var prefix = writer.BeginLengthDelimited();
        var nested = new ProtoShapeSerializer(writer, plan.Shape!, Context.Message);
        schema.SerializeCase(value, ref nested);
        writer.EndLengthDelimited(prefix);
    }

    public readonly void WriteList<TCollection, TElement>(
        int member,
        TCollection value,
        IListSchema<TCollection, TElement> schema
    )
    {
        var plan = Entry(member);
        var list = plan.Shape!;
        if (!list.Packed)
        {
            var repeated = new ProtoShapeSerializer(
                writer,
                list,
                Context.Repeated,
                plan.FieldNumber
            );
            schema.SerializeElements(value, ref repeated);
            return;
        }

        // Packed scalars share one length-delimited field, which an empty list omits entirely.
        var fieldOffset = writer.Length;
        writer.WriteTag(plan.FieldNumber, WireType.Len);
        var prefix = writer.BeginLengthDelimited();
        var packed = new ProtoShapeSerializer(writer, list, Context.Packed);
        schema.SerializeElements(value, ref packed);
        if (writer.Length == prefix + 1)
        {
            writer.Rewind(fieldOffset);
        }
        else
        {
            writer.EndLengthDelimited(prefix);
        }
    }

    public readonly void WriteMap<TDictionary, TValue>(
        int member,
        TDictionary value,
        IMapSchema<TDictionary, TValue> schema
    )
    {
        var plan = Entry(member);
        var entries = new ProtoShapeSerializer(writer, plan.Shape!, Context.Map, plan.FieldNumber);
        schema.SerializeEntries(value, ref entries);
        entries.CloseEntry();
    }
}
