using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Proto;

/// <summary>
/// Protobuf wire types (the low 3 bits of a field tag). NSmithy uses only the four types that
/// appear in proto3: there is no group/start-group (3) or end-group (4) support.
/// </summary>
internal enum WireType : byte
{
    Varint = 0,
    I64 = 1,
    Len = 2,
    I32 = 5,
}

/// <summary>
/// A minimal append-only protobuf encoder. Nested messages, maps, and unions are encoded by
/// serializing the child into its own buffer and embedding it as a length-delimited field, which
/// keeps the writer single-pass and the code easy to follow at the cost of some intermediate
/// allocations — appropriate for the current exploration.
/// </summary>
internal sealed class ProtoWriter
{
    private byte[] buffer = new byte[64];
    private int length;

    public int Length => length;

    public byte[] ToArray() => buffer.AsSpan(0, length).ToArray();

    public void WriteTag(int fieldNumber, WireType wireType) =>
        WriteVarint(((ulong)fieldNumber << 3) | (ulong)wireType);

    public void WriteVarint(ulong value)
    {
        while (value >= 0x80)
        {
            Append((byte)(value | 0x80));
            value >>= 7;
        }

        Append((byte)value);
    }

    public void WriteFixed32(uint value)
    {
        Span<byte> tmp = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(tmp, value);
        Append(tmp);
    }

    public void WriteFixed64(ulong value)
    {
        Span<byte> tmp = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(tmp, value);
        Append(tmp);
    }

    public void WriteLengthDelimited(ReadOnlySpan<byte> value)
    {
        WriteVarint((ulong)value.Length);
        Append(value);
    }

    /// <summary>ZigZag-encodes a signed value for <c>sint32</c>/<c>sint64</c> fields.</summary>
    public static ulong ZigZag(long value) => (ulong)((value << 1) ^ (value >> 63));

    private void Append(byte value)
    {
        EnsureCapacity(1);
        buffer[length++] = value;
    }

    private void Append(ReadOnlySpan<byte> value)
    {
        EnsureCapacity(value.Length);
        value.CopyTo(buffer.AsSpan(length));
        length += value.Length;
    }

    private void EnsureCapacity(int extra)
    {
        if (length + extra <= buffer.Length)
        {
            return;
        }

        var capacity = buffer.Length * 2;
        while (capacity < length + extra)
        {
            capacity *= 2;
        }

        Array.Resize(ref buffer, capacity);
    }
}

/// <summary>
/// A forward-only protobuf decoder over a byte span. Protobuf is not self-describing, so the codec
/// drives reads with the schema; this reader only exposes the primitive wire operations plus a
/// <see cref="SkipField"/> for unknown fields.
/// </summary>
internal ref struct ProtoReader(ReadOnlySpan<byte> buffer)
{
    private readonly ReadOnlySpan<byte> buffer = buffer;
    private int position;

    public readonly bool End => position >= buffer.Length;

    public ulong ReadVarint()
    {
        ulong result = 0;
        var shift = 0;
        while (true)
        {
            if (position >= buffer.Length)
            {
                throw new InvalidOperationException("Truncated protobuf varint.");
            }

            var b = buffer[position++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return result;
            }

            shift += 7;
            if (shift > 63)
            {
                throw new InvalidOperationException("Protobuf varint exceeds 64 bits.");
            }
        }
    }

    public (int FieldNumber, WireType WireType) ReadTag()
    {
        var tag = ReadVarint();
        var fieldNumber = (int)(tag >> 3);
        var wireType = (WireType)(byte)(tag & 0x07);
        if (fieldNumber <= 0)
        {
            throw new InvalidOperationException($"Invalid protobuf field number {fieldNumber}.");
        }

        return (fieldNumber, wireType);
    }

    public uint ReadFixed32()
    {
        var value = BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        return value;
    }

    public ulong ReadFixed64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));

    public ReadOnlySpan<byte> ReadLengthDelimited() => Take((int)ReadVarint());

    public void SkipField(WireType wireType)
    {
        switch (wireType)
        {
            case WireType.Varint:
                ReadVarint();
                break;
            case WireType.I64:
                Take(8);
                break;
            case WireType.Len:
                Take((int)ReadVarint());
                break;
            case WireType.I32:
                Take(4);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported protobuf wire type '{wireType}'."
                );
        }
    }

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || position + count > buffer.Length)
        {
            throw new InvalidOperationException("Truncated protobuf message.");
        }

        var slice = buffer.Slice(position, count);
        position += count;
        return slice;
    }
}

internal static class ProtoWire
{
    private static readonly ShapeId ProtoIndexTrait = new("alloy.proto", "protoIndex");
    private static readonly ShapeId ProtoNumTypeTrait = new("alloy.proto", "protoNumType");
    private static readonly ShapeId ProtoInlinedOneOfTrait = new(
        "alloy.proto",
        "protoInlinedOneOf"
    );
    private static readonly ShapeId SparseTrait = new("smithy.api", "sparse");

    // String enums map to proto enums; the declared values (in declaration order) come from the
    // synthetic trait the codegen attaches, matching ProtoGenerator's UNSPECIFIED=0, then 1,2,3…
    private static readonly ShapeId SyntheticEnumTrait = new("smithy.synthetic", "enum");

    // ---- helpers -----------------------------------------------------------

    private static readonly IReadOnlyDictionary<ShapeId, Trait> NoTraits =
        new Dictionary<ShapeId, Trait>();

    private enum IntEncoding
    {
        VarInt32,
        VarInt64,
        SInt32,
        SInt64,
        UInt32,
        UInt64,
        Fixed32,
        Fixed64,
        SFixed32,
        SFixed64,
    }

    private static IntEncoding IntEncodingOf(
        ShapeKind kind,
        IReadOnlyDictionary<ShapeId, Trait> traits
    )
    {
        var isLong = kind == ShapeKind.Long;
        var numType =
            traits.TryGetValue(ProtoNumTypeTrait, out var trait)
            && trait.Value.Kind == DocumentKind.String
                ? trait.Value.AsString()
                : null;

        return numType switch
        {
            "SIGNED" => isLong ? IntEncoding.SInt64 : IntEncoding.SInt32,
            "UNSIGNED" => isLong ? IntEncoding.UInt64 : IntEncoding.UInt32,
            "FIXED" => isLong ? IntEncoding.Fixed64 : IntEncoding.Fixed32,
            "FIXED_SIGNED" => isLong ? IntEncoding.SFixed64 : IntEncoding.SFixed32,
            _ => isLong ? IntEncoding.VarInt64 : IntEncoding.VarInt32,
        };
    }

    internal static void WriteInteger(
        ProtoWriter writer,
        ShapeKind kind,
        IReadOnlyDictionary<ShapeId, Trait> traits,
        long value
    ) => WriteInteger(writer, IntEncodingOf(kind, traits), value);

    private static void WriteInteger(ProtoWriter writer, IntEncoding encoding, long value)
    {
        switch (encoding)
        {
            case IntEncoding.VarInt32:
            case IntEncoding.VarInt64:
                writer.WriteVarint((ulong)value);
                break;
            case IntEncoding.SInt32:
                writer.WriteVarint(ProtoWriter.ZigZag((int)value));
                break;
            case IntEncoding.SInt64:
                writer.WriteVarint(ProtoWriter.ZigZag(value));
                break;
            case IntEncoding.UInt32:
                writer.WriteVarint((uint)value);
                break;
            case IntEncoding.UInt64:
                writer.WriteVarint((ulong)value);
                break;
            case IntEncoding.Fixed32:
            case IntEncoding.SFixed32:
                writer.WriteFixed32((uint)(int)value);
                break;
            case IntEncoding.Fixed64:
            case IntEncoding.SFixed64:
                writer.WriteFixed64((ulong)value);
                break;
            default:
                throw new InvalidOperationException($"Unknown integer encoding '{encoding}'.");
        }
    }

    internal static long ReadInteger(
        ref ProtoReader reader,
        ShapeKind kind,
        IReadOnlyDictionary<ShapeId, Trait> traits
    ) => ReadInteger(ref reader, IntEncodingOf(kind, traits));

    private static long ReadInteger(ref ProtoReader reader, IntEncoding encoding)
    {
        switch (encoding)
        {
            case IntEncoding.VarInt32:
                return (int)(uint)reader.ReadVarint();
            case IntEncoding.VarInt64:
                return (long)reader.ReadVarint();
            case IntEncoding.SInt32:
                return (int)DecodeZigZag(reader.ReadVarint());
            case IntEncoding.SInt64:
                return DecodeZigZag(reader.ReadVarint());
            case IntEncoding.UInt32:
                return (uint)reader.ReadVarint();
            case IntEncoding.UInt64:
                return (long)reader.ReadVarint();
            case IntEncoding.Fixed32:
                return reader.ReadFixed32();
            case IntEncoding.SFixed32:
                return (int)reader.ReadFixed32();
            case IntEncoding.Fixed64:
            case IntEncoding.SFixed64:
                return (long)reader.ReadFixed64();
            default:
                throw new InvalidOperationException($"Unknown integer encoding '{encoding}'.");
        }
    }

    internal static byte[] EncodeTimestamp(DateTimeOffset value)
    {
        var ticks = value.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks;
        var seconds = Math.DivRem(ticks, TimeSpan.TicksPerSecond, out var remainderTicks);
        if (remainderTicks < 0)
        {
            seconds--;
            remainderTicks += TimeSpan.TicksPerSecond;
        }

        var nanos = (int)remainderTicks * 100;
        var writer = new ProtoWriter();
        if (seconds != 0)
        {
            writer.WriteTag(1, WireType.Varint);
            writer.WriteVarint((ulong)seconds);
        }

        if (nanos != 0)
        {
            writer.WriteTag(2, WireType.Varint);
            writer.WriteVarint((ulong)(long)nanos);
        }

        return writer.ToArray();
    }

    internal static DateTimeOffset DecodeTimestamp(ReadOnlySpan<byte> bytes)
    {
        long seconds = 0;
        var nanos = 0;
        var reader = new ProtoReader(bytes);
        while (!reader.End)
        {
            var (number, wireType) = reader.ReadTag();
            switch (number)
            {
                case 1:
                    seconds = (long)reader.ReadVarint();
                    break;
                case 2:
                    nanos = (int)(long)reader.ReadVarint();
                    break;
                default:
                    reader.SkipField(wireType);
                    break;
            }
        }

        var ticks =
            DateTimeOffset.UnixEpoch.UtcTicks + (seconds * TimeSpan.TicksPerSecond) + (nanos / 100);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    internal static WireType WireTypeOf(
        ShapeKind kind,
        IReadOnlyDictionary<ShapeId, Trait> traits
    ) =>
        kind switch
        {
            ShapeKind.Boolean or ShapeKind.IntEnum or ShapeKind.Enum => WireType.Varint,
            ShapeKind.Byte or ShapeKind.Short or ShapeKind.Integer or ShapeKind.Long =>
                IntEncodingOf(kind, traits) switch
                {
                    IntEncoding.Fixed32 or IntEncoding.SFixed32 => WireType.I32,
                    IntEncoding.Fixed64 or IntEncoding.SFixed64 => WireType.I64,
                    _ => WireType.Varint,
                },
            ShapeKind.Float => WireType.I32,
            ShapeKind.Double => WireType.I64,
            ShapeKind.String
            or ShapeKind.Blob
            or ShapeKind.BigInteger
            or ShapeKind.BigDecimal
            or ShapeKind.Timestamp
            or ShapeKind.Structure
            or ShapeKind.Union
            or ShapeKind.Document => WireType.Len,
            _ => throw new NotSupportedException($"No proto wire type for schema kind '{kind}'."),
        };

    internal static bool IsPackableScalar(ShapeKind kind) =>
        kind
            is ShapeKind.Boolean
                or ShapeKind.Byte
                or ShapeKind.Short
                or ShapeKind.Integer
                or ShapeKind.Long
                or ShapeKind.Float
                or ShapeKind.Double
                or ShapeKind.IntEnum;

    internal static bool IsSparse(Schema schema) => schema.HasTrait(SparseTrait);

    internal static bool IsInlinedUnion(Schema schema) =>
        schema.Kind == ShapeKind.Union && schema.HasTrait(ProtoInlinedOneOfTrait);

    // ---- string enum ordinals ----------------------------------------------

    internal static int EnumOrdinal(Schema enumSchema, string value)
    {
        var members = EnumMembers(enumSchema);
        var index = members.IndexOf(value);
        // Unknown / unmatched maps to the proto UNSPECIFIED = 0 default.
        return index < 0 ? 0 : index + 1;
    }

    internal static string? EnumValueForOrdinal(Schema enumSchema, int ordinal)
    {
        if (ordinal <= 0)
        {
            // 0 is the synthetic proto UNSPECIFIED, which has no Smithy enum member.
            return null;
        }

        var members = EnumMembers(enumSchema);
        return ordinal - 1 < members.Count ? members[ordinal - 1] : null;
    }

    private static List<string> EnumMembers(Schema enumSchema)
    {
        var trait = enumSchema.GetTrait(SyntheticEnumTrait);
        if (trait is not { Value.Kind: DocumentKind.Array })
        {
            throw new NotSupportedException(
                "String enum schema is missing the smithy.synthetic#enum trait required to map "
                    + "values to proto enum field numbers."
            );
        }

        var members = new List<string>();
        foreach (var entry in trait.Value.Value.AsArray())
        {
            if (
                entry.Kind == DocumentKind.Object
                && entry.AsObject().TryGetValue("value", out var v)
                && v.Kind == DocumentKind.String
            )
            {
                members.Add(v.AsString());
            }
        }

        return members;
    }

    // ---- google.protobuf.Value (for @sparse map values and Document) -------

    private const int ValueNullField = 1;
    private const int ValueNumberField = 2;
    private const int ValueStringField = 3;
    private const int ValueBoolField = 4;
    private const int ValueStructField = 5;
    private const int ValueListField = 6;

    /// <summary>Encodes a sparse-map value (a declared scalar, or null) as a google.protobuf.Value.</summary>
    internal static void EncodeScalarValueMessage<T>(ProtoWriter writer, Schema<T> valueSchema, T? value)
    {
        if (value is null)
        {
            writer.WriteTag(ValueNullField, WireType.Varint);
            writer.WriteVarint(0);
            return;
        }

        switch (Unwrap(valueSchema).Kind)
        {
            case ShapeKind.String:
                writer.WriteTag(ValueStringField, WireType.Len);
                writer.WriteLengthDelimited(Encoding.UTF8.GetBytes((string)(object)value));
                break;
            case ShapeKind.Boolean:
                writer.WriteTag(ValueBoolField, WireType.Varint);
                writer.WriteVarint((bool)(object)value ? 1UL : 0UL);
                break;
            case ShapeKind.Byte
            or ShapeKind.Short
            or ShapeKind.Integer
            or ShapeKind.Long
            or ShapeKind.Float
            or ShapeKind.Double:
                writer.WriteTag(ValueNumberField, WireType.I64);
                writer.WriteFixed64(
                    BitConverter.DoubleToUInt64Bits(
                        Convert.ToDouble(value, CultureInfo.InvariantCulture)
                    )
                );
                break;
            default:
                throw new NotSupportedException(
                    $"@sparse map values of kind '{Unwrap(valueSchema).Kind}' are not supported "
                        + "(only scalar values map to google.protobuf.Value)."
                );
        }

    }

    /// <summary>Decodes a google.protobuf.Value back to a sparse-map scalar (or null).</summary>
    internal static T? DecodeScalarValueMessage<T>(Schema<T> valueSchema, ReadOnlySpan<byte> bytes)
    {
        var kind = Unwrap(valueSchema).Kind;
        T? result = default;
        var reader = new ProtoReader(bytes);
        while (!reader.End)
        {
            var (number, wireType) = reader.ReadTag();
            switch (number)
            {
                case ValueNullField:
                    reader.ReadVarint();
                    result = default;
                    break;
                case ValueStringField:
                    result = (T)(object)Encoding.UTF8.GetString(reader.ReadLengthDelimited());
                    break;
                case ValueBoolField:
                    result = (T)(object)(reader.ReadVarint() != 0);
                    break;
                case ValueNumberField:
                    result = CoerceNumber<T>(
                        BitConverter.UInt64BitsToDouble(reader.ReadFixed64()),
                        kind
                    );
                    break;
                default:
                    reader.SkipField(wireType);
                    break;
            }
        }

        return result;
    }

    private static T CoerceNumber<T>(double number, ShapeKind kind) =>
        kind switch
        {
            ShapeKind.Byte => (T)(object)(sbyte)number,
            ShapeKind.Short => (T)(object)(short)number,
            ShapeKind.Integer => (T)(object)(int)number,
            ShapeKind.Long => (T)(object)(long)number,
            ShapeKind.Float => (T)(object)(float)number,
            _ => (T)(object)number,
        };

    internal static void EncodeDocumentValue(ProtoWriter writer, Document document)
    {
        switch (document.Kind)
        {
            case DocumentKind.Null:
                writer.WriteTag(ValueNullField, WireType.Varint);
                writer.WriteVarint(0);
                break;
            case DocumentKind.Boolean:
                writer.WriteTag(ValueBoolField, WireType.Varint);
                writer.WriteVarint(document.AsBoolean() ? 1UL : 0UL);
                break;
            case DocumentKind.Number:
                writer.WriteTag(ValueNumberField, WireType.I64);
                writer.WriteFixed64(BitConverter.DoubleToUInt64Bits((double)document.AsNumber()));
                break;
            case DocumentKind.String:
                writer.WriteTag(ValueStringField, WireType.Len);
                writer.WriteLengthDelimited(Encoding.UTF8.GetBytes(document.AsString()));
                break;
            case DocumentKind.Array:
            {
                var list = new ProtoWriter();
                foreach (var item in document.AsArray())
                {
                    var element = new ProtoWriter();
                    EncodeDocumentValue(element, item);
                    list.WriteTag(1, WireType.Len);
                    list.WriteLengthDelimited(element.ToArray());
                }

                writer.WriteTag(ValueListField, WireType.Len);
                writer.WriteLengthDelimited(list.ToArray());
                break;
            }
            case DocumentKind.Object:
            {
                var structWriter = new ProtoWriter();
                foreach (var (key, item) in document.AsObject())
                {
                    var entry = new ProtoWriter();
                    entry.WriteTag(1, WireType.Len);
                    entry.WriteLengthDelimited(Encoding.UTF8.GetBytes(key));
                    var element = new ProtoWriter();
                    EncodeDocumentValue(element, item);
                    entry.WriteTag(2, WireType.Len);
                    entry.WriteLengthDelimited(element.ToArray());
                    structWriter.WriteTag(1, WireType.Len);
                    structWriter.WriteLengthDelimited(entry.ToArray());
                }

                writer.WriteTag(ValueStructField, WireType.Len);
                writer.WriteLengthDelimited(structWriter.ToArray());
                break;
            }
            default:
                throw new NotSupportedException($"Cannot encode document kind '{document.Kind}'.");
        }
    }

    internal static Document DecodeDocumentValue(ReadOnlySpan<byte> bytes)
    {
        var reader = new ProtoReader(bytes);
        var result = Document.Null;
        while (!reader.End)
        {
            var (number, wireType) = reader.ReadTag();
            switch (number)
            {
                case ValueNullField:
                    reader.ReadVarint();
                    result = Document.Null;
                    break;
                case ValueNumberField:
                    result = Document.From(
                        (decimal)BitConverter.UInt64BitsToDouble(reader.ReadFixed64())
                    );
                    break;
                case ValueStringField:
                    result = Document.From(Encoding.UTF8.GetString(reader.ReadLengthDelimited()));
                    break;
                case ValueBoolField:
                    result = Document.From(reader.ReadVarint() != 0);
                    break;
                case ValueStructField:
                    result = DecodeDocumentStruct(reader.ReadLengthDelimited());
                    break;
                case ValueListField:
                    result = DecodeDocumentList(reader.ReadLengthDelimited());
                    break;
                default:
                    reader.SkipField(wireType);
                    break;
            }
        }

        return result;
    }

    private static Document DecodeDocumentStruct(ReadOnlySpan<byte> bytes)
    {
        var fields = new Dictionary<string, Document>(StringComparer.Ordinal);
        var reader = new ProtoReader(bytes);
        while (!reader.End)
        {
            var (number, wireType) = reader.ReadTag();
            if (number == 1)
            {
                var (key, value) = DecodeStructEntry(reader.ReadLengthDelimited());
                fields[key] = value;
            }
            else
            {
                reader.SkipField(wireType);
            }
        }

        return Document.From(fields);
    }

    private static (string Key, Document Value) DecodeStructEntry(ReadOnlySpan<byte> bytes)
    {
        var key = string.Empty;
        var value = Document.Null;
        var reader = new ProtoReader(bytes);
        while (!reader.End)
        {
            var (number, wireType) = reader.ReadTag();
            switch (number)
            {
                case 1:
                    key = Encoding.UTF8.GetString(reader.ReadLengthDelimited());
                    break;
                case 2:
                    value = DecodeDocumentValue(reader.ReadLengthDelimited());
                    break;
                default:
                    reader.SkipField(wireType);
                    break;
            }
        }

        return (key, value);
    }

    private static Document DecodeDocumentList(ReadOnlySpan<byte> bytes)
    {
        var items = new List<Document>();
        var reader = new ProtoReader(bytes);
        while (!reader.End)
        {
            var (number, wireType) = reader.ReadTag();
            if (number == 1)
            {
                items.Add(DecodeDocumentValue(reader.ReadLengthDelimited()));
            }
            else
            {
                reader.SkipField(wireType);
            }
        }

        return Document.From(items);
    }

    internal static int ProtoIndex(IReadOnlyDictionary<ShapeId, Trait> traits)
    {
        if (
            traits.TryGetValue(ProtoIndexTrait, out var trait)
            && trait.Value.Kind == DocumentKind.Number
        )
        {
            return (int)trait.Value.AsNumber();
        }

        throw new InvalidOperationException(
            "Proto codec requires every member to carry an @protoIndex trait."
        );
    }

    private static long DecodeZigZag(ulong value) => (long)(value >> 1) ^ -(long)(value & 1);

    internal static Schema Unwrap(Schema schema)
    {
        var resolved = schema.Resolved;
        return resolved is INullableSchema nullable ? nullable.Target.Resolved : resolved;
    }
}
