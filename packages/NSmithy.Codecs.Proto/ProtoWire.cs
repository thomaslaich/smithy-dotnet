using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Frozen;
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
/// A minimal append-only protobuf encoder backed by a pooled buffer. Nested messages reserve their
/// length prefix in this buffer and backpatch it when complete.
/// </summary>
internal sealed class ProtoWriter : IDisposable
{
    private byte[] buffer;
    private int length;

    public ProtoWriter(int initialCapacity = 64)
    {
        buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, 64));
    }

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

    public void WriteLengthDelimitedUtf8(string value)
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        WriteVarint((ulong)byteCount);
        EnsureCapacity(byteCount);
        length += Encoding.UTF8.GetBytes(value, buffer.AsSpan(length, byteCount));
    }

    public int BeginLengthDelimited()
    {
        var prefixOffset = length;
        Append(0);
        return prefixOffset;
    }

    public void EndLengthDelimited(int prefixOffset)
    {
        if ((uint)prefixOffset >= (uint)length)
        {
            throw new ArgumentOutOfRangeException(nameof(prefixOffset));
        }

        var valueLength = length - prefixOffset - 1;
        var prefixLength = VarintLength((ulong)valueLength);
        if (prefixLength > 1)
        {
            var shift = prefixLength - 1;
            EnsureCapacity(shift);
            buffer
                .AsSpan(prefixOffset + 1, valueLength)
                .CopyTo(buffer.AsSpan(prefixOffset + prefixLength));
            length += shift;
        }

        WriteVarintAt(prefixOffset, (ulong)valueLength);
    }

    public void Rewind(int position)
    {
        if ((uint)position > (uint)length)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        length = position;
    }

    public void Dispose()
    {
        var rented = buffer;
        buffer = [];
        length = 0;
        if (rented.Length != 0)
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public void Reset(int initialCapacity)
    {
        if (buffer.Length != 0)
        {
            throw new InvalidOperationException("Proto writer is already in use.");
        }

        buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, 64));
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
        ObjectDisposedException.ThrowIf(buffer.Length == 0, this);

        if (length + extra <= buffer.Length)
        {
            return;
        }

        var capacity = buffer.Length * 2;
        while (capacity < length + extra)
        {
            capacity *= 2;
        }

        var replacement = ArrayPool<byte>.Shared.Rent(capacity);
        buffer.AsSpan(0, length).CopyTo(replacement);
        ArrayPool<byte>.Shared.Return(buffer);
        buffer = replacement;
    }

    private static int VarintLength(ulong value)
    {
        var result = 1;
        while (value >= 0x80)
        {
            result++;
            value >>= 7;
        }

        return result;
    }

    private void WriteVarintAt(int offset, ulong value)
    {
        while (value >= 0x80)
        {
            buffer[offset++] = (byte)(value | 0x80);
            value >>= 7;
        }

        buffer[offset] = (byte)value;
    }
}

internal static class ProtoWriterCache
{
    [ThreadStatic]
    private static ProtoWriter? cached;

    public static ProtoWriter Rent(int initialCapacity)
    {
        var writer = cached;
        cached = null;
        if (writer is null)
        {
            return new ProtoWriter(initialCapacity);
        }

        writer.Reset(initialCapacity);
        return writer;
    }

    public static void Return(ProtoWriter writer)
    {
        writer.Dispose();
        cached ??= writer;
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

    /// <summary>The namespace of the shapes the runtime owns rather than reads from a model.</summary>
    private const string FrameworkNamespace = "smithy.framework";

    // String enums map to proto enums; the declared values (in declaration order) come from the
    // synthetic trait the codegen attaches, matching ProtoGenerator's UNSPECIFIED=0, then 1,2,3…
    private static readonly ShapeId SyntheticEnumTrait = new("smithy.synthetic", "enum");

    // ---- helpers -----------------------------------------------------------

    private static readonly IReadOnlyDictionary<ShapeId, Trait> NoTraits =
        new Dictionary<ShapeId, Trait>();

    internal enum IntEncoding
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

    internal static IntEncoding IntEncodingOf(
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

    internal static void WriteInteger(ProtoWriter writer, IntEncoding encoding, long value)
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

    internal static long ReadInteger(ref ProtoReader reader, IntEncoding encoding)
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

    internal static void EncodeTimestamp(ProtoWriter writer, DateTimeOffset value)
    {
        var ticks = value.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks;
        var seconds = Math.DivRem(ticks, TimeSpan.TicksPerSecond, out var remainderTicks);
        if (remainderTicks < 0)
        {
            seconds--;
            remainderTicks += TimeSpan.TicksPerSecond;
        }

        var nanos = (int)remainderTicks * 100;
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

    /// <summary>Each declared value's proto ordinal: declaration order, 1-based, 0 being UNSPECIFIED.</summary>
    internal static FrozenDictionary<string, int> EnumOrdinals(Schema enumSchema)
    {
        var members = EnumMembers(enumSchema);
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < members.Count; i++)
        {
            ordinals.TryAdd(members[i], i + 1);
        }

        return ordinals.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>The declared values by ordinal minus one.</summary>
    internal static string[] EnumValues(Schema enumSchema) => [.. EnumMembers(enumSchema)];

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
    internal static void EncodeScalarValueMessage<T>(
        ProtoWriter writer,
        Schema<T> valueSchema,
        T? value
    )
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
                writer.WriteLengthDelimitedUtf8((string)(object)value);
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
                writer.WriteLengthDelimitedUtf8(document.AsString());
                break;
            case DocumentKind.Array:
            {
                writer.WriteTag(ValueListField, WireType.Len);
                var listPrefix = writer.BeginLengthDelimited();
                foreach (var item in document.AsArray())
                {
                    writer.WriteTag(1, WireType.Len);
                    var elementPrefix = writer.BeginLengthDelimited();
                    EncodeDocumentValue(writer, item);
                    writer.EndLengthDelimited(elementPrefix);
                }

                writer.EndLengthDelimited(listPrefix);
                break;
            }
            case DocumentKind.Object:
            {
                writer.WriteTag(ValueStructField, WireType.Len);
                var structPrefix = writer.BeginLengthDelimited();
                foreach (var (key, item) in document.AsObject())
                {
                    writer.WriteTag(1, WireType.Len);
                    var entryPrefix = writer.BeginLengthDelimited();
                    writer.WriteTag(1, WireType.Len);
                    writer.WriteLengthDelimitedUtf8(key);
                    writer.WriteTag(2, WireType.Len);
                    var elementPrefix = writer.BeginLengthDelimited();
                    EncodeDocumentValue(writer, item);
                    writer.EndLengthDelimited(elementPrefix);
                    writer.EndLengthDelimited(entryPrefix);
                }

                writer.EndLengthDelimited(structPrefix);
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

    /// <summary>
    /// The proto field number for a member: its <c>@protoIndex</c>, which a model has to state
    /// because proto's compatibility rules hang on it — reordering members must not move a field.
    /// </summary>
    /// <remarks>
    /// A framework shape is the one exception. The server runtime can return
    /// <c>smithy.framework#ValidationException</c> from any operation, so every codec has to be able
    /// to put it on the wire, but it comes from the runtime rather than a model file and so has no
    /// trait to carry. Its numbers come from declaration order instead, which is this codec's rule
    /// to make: nothing outside it should have to know that proto wants a number per member.
    /// </remarks>
    internal static int FieldNumber(
        ShapeId memberId,
        IReadOnlyDictionary<ShapeId, Trait> traits,
        int ordinal
    )
    {
        if (
            traits.TryGetValue(ProtoIndexTrait, out var trait)
            && trait.Value.Kind == DocumentKind.Number
        )
        {
            return (int)trait.Value.AsNumber();
        }

        if (memberId.Namespace == FrameworkNamespace)
        {
            return ordinal + 1;
        }

        throw new InvalidOperationException(
            $"Proto codec requires every member to carry an @protoIndex trait; '{memberId}' has none."
        );
    }

    private static long DecodeZigZag(ulong value) => (long)(value >> 1) ^ -(long)(value & 1);

    internal static Schema Unwrap(Schema schema)
    {
        var resolved = schema.Resolved;
        return resolved is INullableSchema nullable ? nullable.Target.Resolved : resolved;
    }
}
