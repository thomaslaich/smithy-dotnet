using System.Buffers.Binary;

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
