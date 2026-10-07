using System.Globalization;
using System.Numerics;
using System.Text;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Proto;

/// <summary>One field occurrence: where its value lies in the payload, and how it is encoded.</summary>
internal readonly record struct ProtoOccurrence(int Start, int Length, WireType WireType);

/// <summary>
/// Reads one protobuf value: a range of the payload, described by the plan of the field it
/// belongs to. A repeated or map field instead reads every occurrence the enclosing message held,
/// since protobuf lets a message repeat a field anywhere among its others.
/// </summary>
internal readonly struct ProtoShapeDeserializer : IShapeDeserializer
{
    private readonly byte[] payload;
    private readonly ProtoOccurrence value;
    private readonly ProtoMemberPlan entry;

    // The field number a repeated or map field is read under, scanned for across the range of the
    // enclosing message in `value`; 0 when this reads one value.
    private readonly int repeatedField;

    // The case an inlined oneof's field belongs to, or -1.
    private readonly int @case;

    // Whether the value is a sparse map's google.protobuf.Value rather than the value itself.
    private readonly bool sparse;

    public ProtoShapeDeserializer(
        byte[] payload,
        ProtoOccurrence value,
        ProtoMemberPlan entry,
        int @case = -1,
        bool sparse = false
    )
    {
        this.payload = payload;
        this.value = value;
        this.entry = entry;
        this.@case = @case;
        this.sparse = sparse;
    }

    private ProtoShapeDeserializer(
        byte[] payload,
        ProtoMemberPlan entry,
        int repeatedField,
        ProtoOccurrence range
    )
    {
        this.payload = payload;
        this.entry = entry;
        this.repeatedField = repeatedField;
        value = range;
        @case = -1;
    }

    private ReadOnlySpan<byte> Bytes => payload.AsSpan(value.Start, value.Length);

    private ProtoReader Reader => new(Bytes);

    public bool TryReadNull() => sparse && ProtoSparseValues.IsNull(Bytes);

    public bool ReadBoolean() =>
        sparse ? ProtoSparseValues.ReadBoolean(Bytes) : Reader.ReadVarint() != 0;

    public sbyte ReadByte() => (sbyte)ReadInt64();

    public short ReadShort() => (short)ReadInt64();

    public int ReadInteger() => (int)ReadInt64();

    public long ReadLong() => ReadInt64();

    private long ReadInt64()
    {
        if (sparse)
        {
            return (long)ProtoSparseValues.ReadNumber(Bytes);
        }

        var reader = Reader;
        return ProtoWire.ReadInteger(ref reader, entry.Encoding);
    }

    public float ReadFloat() =>
        sparse
            ? (float)ProtoSparseValues.ReadNumber(Bytes)
            : BitConverter.UInt32BitsToSingle(Reader.ReadFixed32());

    public double ReadDouble() =>
        sparse
            ? ProtoSparseValues.ReadNumber(Bytes)
            : BitConverter.UInt64BitsToDouble(Reader.ReadFixed64());

    public BigInteger ReadBigInteger() =>
        BigInteger.Parse(Encoding.UTF8.GetString(Bytes), CultureInfo.InvariantCulture);

    public decimal ReadBigDecimal() =>
        decimal.Parse(Encoding.UTF8.GetString(Bytes), CultureInfo.InvariantCulture);

    public string ReadString() =>
        sparse ? ProtoSparseValues.ReadString(Bytes)! : Encoding.UTF8.GetString(Bytes);

    public byte[] ReadBlob() => Bytes.ToArray();

    public DateTimeOffset ReadTimestamp() => ProtoWire.DecodeTimestamp(Bytes);

    public Document ReadDocument() => ProtoWire.DecodeDocumentValue(Bytes);

    // 0 is the synthetic proto UNSPECIFIED, which has no Smithy enum member; it and any ordinal
    // this model does not know read as no value.
    public string ReadStringEnum()
    {
        var ordinal = (int)Reader.ReadVarint();
        var values = entry.EnumValues!;
        return ordinal > 0 && ordinal <= values.Length ? values[ordinal - 1] : null!;
    }

    public int ReadIntEnum() => (int)Reader.ReadVarint();

    public Stream ReadStream() =>
        throw new NotSupportedException("Proto codec does not support streaming blob schemas.");

    public IAsyncEnumerable<TEvent> ReadEventStream<TEvent>(Schema<TEvent> eventSchema) =>
        throw new NotSupportedException("Proto codec does not support event stream schemas.");

    public T ReadStruct<T, TBuilder>(StructSchema<T, TBuilder> schema)
    {
        var builder = schema.CreateTypedBuilder();
        ReadMessage(payload, value, entry.Shape!, schema, builder);
        return schema.Build(builder);
    }

    /// <summary>
    /// Reads a message's fields into <paramref name="builder"/> in one pass. A field that occurs
    /// more than once is read each time, so the last occurrence wins. A repeated or map field is
    /// read once, at its first occurrence, by a deserializer that scans the rest of the message for
    /// all of them, since protobuf lets other fields come between.
    /// </summary>
    private static void ReadMessage<T, TBuilder>(
        byte[] payload,
        ProtoOccurrence message,
        ProtoShapePlan plan,
        StructSchema<T, TBuilder> schema,
        TBuilder builder
    )
    {
        var members = plan.Members;
        Span<bool> collectionsRead =
            members.Length <= 256 ? stackalloc bool[members.Length] : new bool[members.Length];

        var reader = new ProtoReader(payload.AsSpan(message.Start, message.Length));
        while (!reader.End)
        {
            var tagStart = reader.Position;
            var (number, wireType) = reader.ReadTag();
            if (!plan.TryGetField(number, out var field))
            {
                reader.SkipField(wireType);
                continue;
            }

            var member = members[field.Member];
            if (member.Kind is ShapeKind.List or ShapeKind.Set or ShapeKind.Map)
            {
                if (!collectionsRead[field.Member])
                {
                    collectionsRead[field.Member] = true;
                    var all = new ProtoShapeDeserializer(
                        payload,
                        member,
                        number,
                        new ProtoOccurrence(
                            message.Start + tagStart,
                            message.Length - tagStart,
                            WireType.Len
                        )
                    );
                    schema.DeserializeMember(builder, field.Member, ref all);
                }

                reader.SkipField(wireType);
                continue;
            }

            var (start, length) = reader.ReadValueRange(wireType);
            var nested = new ProtoShapeDeserializer(
                payload,
                new ProtoOccurrence(message.Start + start, length, wireType),
                member,
                field.Case
            );
            try
            {
                schema.DeserializeMember(builder, field.Member, ref nested);
            }
            catch (MissingRequiredMemberException exception)
            {
                exception.PrependPathToken(member.Name);
                throw;
            }
        }

        // An absent repeated or map field is an empty collection, not an absent one.
        for (var index = 0; index < members.Length; index++)
        {
            var member = members[index];
            if (
                member.Kind is ShapeKind.List or ShapeKind.Set or ShapeKind.Map
                && !collectionsRead[index]
            )
            {
                var none = new ProtoShapeDeserializer(
                    payload,
                    member,
                    member.FieldNumber,
                    new ProtoOccurrence(message.Start + message.Length, 0, WireType.Len)
                );
                schema.DeserializeMember(builder, index, ref none);
            }
        }
    }

    public TCollection ReadList<TCollection, TElement, TBuilder>(
        ListSchema<TCollection, TElement, TBuilder> schema
    )
    {
        var plan = entry.Shape!;
        var element = plan.Members[0];
        var builder = schema.CreateTypedBuilder();
        var fields = new ProtoReader(payload.AsSpan(value.Start, value.Length));
        while (!fields.End)
        {
            var (fieldNumber, fieldWireType) = fields.ReadTag();
            if (fieldNumber != repeatedField)
            {
                fields.SkipField(fieldWireType);
                continue;
            }

            var (fieldStart, fieldLength) = fields.ReadValueRange(fieldWireType);
            var occurrence = new ProtoOccurrence(
                value.Start + fieldStart,
                fieldLength,
                fieldWireType
            );
            // A packable element may arrive packed in one length-delimited field or one per field.
            if (occurrence.WireType == WireType.Len && plan.Packed)
            {
                var packed = new ProtoReader(payload.AsSpan(occurrence.Start, occurrence.Length));
                while (!packed.End)
                {
                    var (start, length) = packed.ReadValueRange(element.WireType);
                    var item = new ProtoShapeDeserializer(
                        payload,
                        new ProtoOccurrence(occurrence.Start + start, length, element.WireType),
                        element
                    );
                    schema.DeserializeElement(builder, ref item);
                }

                continue;
            }

            var single = new ProtoShapeDeserializer(payload, occurrence, element);
            schema.DeserializeElement(builder, ref single);
        }

        return schema.Build(builder);
    }

    public TDictionary ReadMap<TDictionary, TValue, TBuilder>(
        MapSchema<TDictionary, TValue, TBuilder> schema
    )
    {
        var plan = entry.Shape!;
        var valuePlan = plan.Members[1];
        var builder = schema.CreateTypedBuilder();
        var fields = new ProtoReader(payload.AsSpan(value.Start, value.Length));
        while (!fields.End)
        {
            var (fieldNumber, fieldWireType) = fields.ReadTag();
            if (fieldNumber != repeatedField)
            {
                fields.SkipField(fieldWireType);
                continue;
            }

            var (fieldStart, fieldLength) = fields.ReadValueRange(fieldWireType);
            var occurrence = new ProtoOccurrence(
                value.Start + fieldStart,
                fieldLength,
                fieldWireType
            );
            var key = string.Empty;
            ProtoOccurrence? entryValue = null;
            var reader = new ProtoReader(payload.AsSpan(occurrence.Start, occurrence.Length));
            while (!reader.End)
            {
                var (number, wireType) = reader.ReadTag();
                switch (number)
                {
                    case 1:
                        var (keyStart, keyLength) = reader.ReadValueRange(wireType);
                        key = Encoding.UTF8.GetString(
                            payload.AsSpan(occurrence.Start + keyStart, keyLength)
                        );
                        break;
                    case 2:
                        var (start, length) = reader.ReadValueRange(wireType);
                        entryValue = new ProtoOccurrence(
                            occurrence.Start + start,
                            length,
                            wireType
                        );
                        break;
                    default:
                        reader.SkipField(wireType);
                        break;
                }
            }

            if (entryValue is not { } present)
            {
                schema.Add(builder, key, default!);
                continue;
            }

            var nested = new ProtoShapeDeserializer(
                payload,
                present,
                valuePlan,
                sparse: plan.Sparse
            );
            schema.DeserializeEntry(builder, key, ref nested);
        }

        return schema.Build(builder);
    }

    public T ReadUnion<T>(UnionSchema<T> schema)
    {
        var plan = entry.Shape!;
        // An inlined oneof's field was found among the enclosing message's fields.
        if (@case >= 0)
        {
            var inlined = new ProtoShapeDeserializer(payload, value, plan.Members[@case]);
            return schema.DeserializeCase(@case, ref inlined);
        }

        // A union message holds one case's field; if several arrive, the last wins. A case this
        // model does not know (a newer peer's) reads as no value.
        (ProtoOccurrence Value, int Case)? found = null;
        var reader = Reader;
        while (!reader.End)
        {
            var (number, wireType) = reader.ReadTag();
            if (!plan.TryGetField(number, out var field))
            {
                reader.SkipField(wireType);
                continue;
            }

            var (start, length) = reader.ReadValueRange(wireType);
            found = (new ProtoOccurrence(value.Start + start, length, wireType), field.Member);
        }

        if (found is not { } match)
        {
            return default!;
        }

        var nested = new ProtoShapeDeserializer(payload, match.Value, plan.Members[match.Case]);
        return schema.DeserializeCase(match.Case, ref nested);
    }
}

/// <summary>Reads the google.protobuf.Value a sparse map value is encoded as.</summary>
internal static class ProtoSparseValues
{
    private const int NumberField = 2;
    private const int StringField = 3;
    private const int BoolField = 4;

    public static bool IsNull(ReadOnlySpan<byte> bytes)
    {
        var reader = new ProtoReader(bytes);
        while (!reader.End)
        {
            var (number, wireType) = reader.ReadTag();
            if (number is NumberField or StringField or BoolField)
            {
                return false;
            }

            reader.SkipField(wireType);
        }

        return true;
    }

    public static double ReadNumber(ReadOnlySpan<byte> bytes)
    {
        var reader = new ProtoReader(bytes);
        double result = 0;
        while (!reader.End)
        {
            var (number, wireType) = reader.ReadTag();
            if (number == NumberField)
            {
                result = BitConverter.UInt64BitsToDouble(reader.ReadFixed64());
            }
            else
            {
                reader.SkipField(wireType);
            }
        }

        return result;
    }

    public static string? ReadString(ReadOnlySpan<byte> bytes)
    {
        var reader = new ProtoReader(bytes);
        string? result = null;
        while (!reader.End)
        {
            var (number, wireType) = reader.ReadTag();
            if (number == StringField)
            {
                result = Encoding.UTF8.GetString(reader.ReadLengthDelimited());
            }
            else
            {
                reader.SkipField(wireType);
            }
        }

        return result;
    }

    public static bool ReadBoolean(ReadOnlySpan<byte> bytes)
    {
        var reader = new ProtoReader(bytes);
        var result = false;
        while (!reader.End)
        {
            var (number, wireType) = reader.ReadTag();
            if (number == BoolField)
            {
                result = reader.ReadVarint() != 0;
            }
            else
            {
                reader.SkipField(wireType);
            }
        }

        return result;
    }
}
