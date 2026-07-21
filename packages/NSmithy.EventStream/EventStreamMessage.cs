using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;

namespace NSmithy.EventStream;

/// <summary>
/// One <c>application/vnd.amazon.eventstream</c> message: typed headers plus an opaque payload.
///
/// Wire layout: a 12-byte prelude (total length, headers length, CRC32 of the first 8 bytes),
/// the headers block, the payload, and a trailing CRC32 over everything before it. All integers
/// are big-endian; both CRCs are standard IEEE CRC-32. This type owns framing only — the payload
/// is encoded/decoded by a protocol's body codec, and Smithy semantics (event types, exception
/// events, initial messages) live in the protocols.
/// </summary>
public sealed class EventStreamMessage
{
    private const int PreludeLength = 12;
    private const int MessageCrcLength = 4;
    private const int MaxHeaderNameLength = byte.MaxValue;

    public EventStreamMessage(
        IReadOnlyDictionary<string, EventStreamHeaderValue> headers,
        ReadOnlyMemory<byte> payload
    )
    {
        ArgumentNullException.ThrowIfNull(headers);
        Headers = headers;
        Payload = payload;
    }

    public IReadOnlyDictionary<string, EventStreamHeaderValue> Headers { get; }

    public ReadOnlyMemory<byte> Payload { get; }

    /// <summary>Convenience accessor: the header's string value, or null when absent or non-string.</summary>
    public string? StringHeader(string name) =>
        Headers.TryGetValue(name, out var value) && value is EventStreamHeaderValue.Text text
            ? text.Value
            : null;

    /// <summary>Encodes the message into its framed wire form.</summary>
    public byte[] Encode()
    {
        var headersBlock = EncodeHeaders(Headers);
        var totalLength = PreludeLength + headersBlock.Length + Payload.Length + MessageCrcLength;

        var message = new byte[totalLength];
        BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(0, 4), (uint)totalLength);
        BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(4, 4), (uint)headersBlock.Length);
        BinaryPrimitives.WriteUInt32BigEndian(
            message.AsSpan(8, 4),
            Crc32.HashToUInt32(message.AsSpan(0, 8))
        );
        headersBlock.CopyTo(message.AsSpan(PreludeLength));
        Payload.Span.CopyTo(message.AsSpan(PreludeLength + headersBlock.Length));
        BinaryPrimitives.WriteUInt32BigEndian(
            message.AsSpan(totalLength - MessageCrcLength, 4),
            Crc32.HashToUInt32(message.AsSpan(0, totalLength - MessageCrcLength))
        );
        return message;
    }

    /// <summary>
    /// Decodes one complete framed message, validating both CRCs.
    /// Throws <see cref="InvalidDataException"/> on corruption or truncation.
    /// </summary>
    public static EventStreamMessage Decode(ReadOnlyMemory<byte> message)
    {
        var span = message.Span;
        if (span.Length < PreludeLength + MessageCrcLength)
        {
            throw new InvalidDataException("Truncated event stream message prelude.");
        }

        var totalLength = ReadLength(span[..4], "total length");
        var headersLength = ReadLength(span.Slice(4, 4), "headers length");
        var preludeCrc = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(8, 4));
        if (Crc32.HashToUInt32(span[..8]) != preludeCrc)
        {
            throw new InvalidDataException("Event stream prelude failed CRC validation.");
        }

        if (span.Length < totalLength)
        {
            throw new InvalidDataException("Truncated event stream message.");
        }

        var payloadLength = totalLength - PreludeLength - headersLength - MessageCrcLength;
        if (payloadLength < 0)
        {
            throw new InvalidDataException(
                "Event stream headers length exceeds the message length."
            );
        }

        var messageCrc = BinaryPrimitives.ReadUInt32BigEndian(
            span.Slice(totalLength - MessageCrcLength, 4)
        );
        if (Crc32.HashToUInt32(span[..(totalLength - MessageCrcLength)]) != messageCrc)
        {
            throw new InvalidDataException("Event stream message failed CRC validation.");
        }

        var headers = DecodeHeaders(span.Slice(PreludeLength, headersLength));
        var payload = message.Slice(PreludeLength + headersLength, payloadLength);
        return new EventStreamMessage(headers, payload);
    }

    internal static int ReadLength(ReadOnlySpan<byte> lengthBytes, string what)
    {
        var length = BinaryPrimitives.ReadUInt32BigEndian(lengthBytes);
        if (length > int.MaxValue)
        {
            throw new InvalidDataException(
                $"Event stream {what} {length} exceeds the maximum supported size."
            );
        }

        return (int)length;
    }

    private static byte[] EncodeHeaders(IReadOnlyDictionary<string, EventStreamHeaderValue> headers)
    {
        using var buffer = new MemoryStream();
        foreach (var (name, value) in headers)
        {
            var nameBytes = Encoding.UTF8.GetBytes(name);
            if (nameBytes.Length is 0 or > MaxHeaderNameLength)
            {
                throw new ArgumentException(
                    $"Event stream header name '{name}' must be 1-{MaxHeaderNameLength} bytes."
                );
            }

            buffer.WriteByte((byte)nameBytes.Length);
            buffer.Write(nameBytes);
            EncodeValue(buffer, name, value);
        }

        return buffer.ToArray();
    }

    private static void EncodeValue(MemoryStream buffer, string name, EventStreamHeaderValue value)
    {
        switch (value)
        {
            case EventStreamHeaderValue.Bool boolean:
                buffer.WriteByte(
                    (byte)(boolean.Value ? HeaderType.BoolTrue : HeaderType.BoolFalse)
                );
                break;
            case EventStreamHeaderValue.Signed8 int8:
                buffer.WriteByte((byte)HeaderType.Byte);
                buffer.WriteByte(unchecked((byte)int8.Value));
                break;
            case EventStreamHeaderValue.Signed16 int16:
                buffer.WriteByte((byte)HeaderType.Int16);
                WriteBigEndian(
                    buffer,
                    stackalloc byte[2],
                    span => BinaryPrimitives.WriteInt16BigEndian(span, int16.Value)
                );
                break;
            case EventStreamHeaderValue.Signed32 int32:
                buffer.WriteByte((byte)HeaderType.Int32);
                WriteBigEndian(
                    buffer,
                    stackalloc byte[4],
                    span => BinaryPrimitives.WriteInt32BigEndian(span, int32.Value)
                );
                break;
            case EventStreamHeaderValue.Signed64 int64:
                buffer.WriteByte((byte)HeaderType.Int64);
                WriteBigEndian(
                    buffer,
                    stackalloc byte[8],
                    span => BinaryPrimitives.WriteInt64BigEndian(span, int64.Value)
                );
                break;
            case EventStreamHeaderValue.Blob bytes:
                buffer.WriteByte((byte)HeaderType.ByteArray);
                WriteLengthPrefixed(buffer, name, bytes.Value);
                break;
            case EventStreamHeaderValue.Text text:
                buffer.WriteByte((byte)HeaderType.String);
                WriteLengthPrefixed(buffer, name, Encoding.UTF8.GetBytes(text.Value));
                break;
            case EventStreamHeaderValue.Timestamp timestamp:
                buffer.WriteByte((byte)HeaderType.Timestamp);
                WriteBigEndian(
                    buffer,
                    stackalloc byte[8],
                    span =>
                        BinaryPrimitives.WriteInt64BigEndian(
                            span,
                            timestamp.Value.ToUnixTimeMilliseconds()
                        )
                );
                break;
            case EventStreamHeaderValue.Uuid uuid:
                buffer.WriteByte((byte)HeaderType.Uuid);
                Span<byte> guidBytes = stackalloc byte[16];
                uuid.Value.TryWriteBytes(guidBytes, bigEndian: true, out _);
                buffer.Write(guidBytes);
                break;
            default:
                throw new ArgumentException($"Unknown event stream header value: {value}.");
        }
    }

    private delegate void SpanWriter(Span<byte> span);

    private static void WriteBigEndian(MemoryStream buffer, Span<byte> scratch, SpanWriter write)
    {
        write(scratch);
        buffer.Write(scratch);
    }

    private static void WriteLengthPrefixed(MemoryStream buffer, string name, byte[] value)
    {
        if (value.Length > ushort.MaxValue)
        {
            throw new ArgumentException(
                $"Event stream header '{name}' value exceeds {ushort.MaxValue} bytes."
            );
        }

        Span<byte> length = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)value.Length);
        buffer.Write(length);
        buffer.Write(value);
    }

    private static Dictionary<string, EventStreamHeaderValue> DecodeHeaders(
        ReadOnlySpan<byte> block
    )
    {
        var headers = new Dictionary<string, EventStreamHeaderValue>(StringComparer.Ordinal);
        var offset = 0;
        while (offset < block.Length)
        {
            var nameLength = block[offset];
            offset += 1;
            var name = Encoding.UTF8.GetString(Slice(block, ref offset, nameLength));
            var type = (HeaderType)Slice(block, ref offset, 1)[0];
            headers[name] = DecodeValue(block, ref offset, name, type);
        }

        return headers;
    }

    private static EventStreamHeaderValue DecodeValue(
        ReadOnlySpan<byte> block,
        ref int offset,
        string name,
        HeaderType type
    )
    {
        switch (type)
        {
            case HeaderType.BoolTrue:
                return new EventStreamHeaderValue.Bool(true);
            case HeaderType.BoolFalse:
                return new EventStreamHeaderValue.Bool(false);
            case HeaderType.Byte:
                return new EventStreamHeaderValue.Signed8(
                    unchecked((sbyte)Slice(block, ref offset, 1)[0])
                );
            case HeaderType.Int16:
                return new EventStreamHeaderValue.Signed16(
                    BinaryPrimitives.ReadInt16BigEndian(Slice(block, ref offset, 2))
                );
            case HeaderType.Int32:
                return new EventStreamHeaderValue.Signed32(
                    BinaryPrimitives.ReadInt32BigEndian(Slice(block, ref offset, 4))
                );
            case HeaderType.Int64:
                return new EventStreamHeaderValue.Signed64(
                    BinaryPrimitives.ReadInt64BigEndian(Slice(block, ref offset, 8))
                );
            case HeaderType.ByteArray:
            {
                var length = BinaryPrimitives.ReadUInt16BigEndian(Slice(block, ref offset, 2));
                return new EventStreamHeaderValue.Blob(Slice(block, ref offset, length).ToArray());
            }
            case HeaderType.String:
            {
                var length = BinaryPrimitives.ReadUInt16BigEndian(Slice(block, ref offset, 2));
                return new EventStreamHeaderValue.Text(
                    Encoding.UTF8.GetString(Slice(block, ref offset, length))
                );
            }
            case HeaderType.Timestamp:
                return new EventStreamHeaderValue.Timestamp(
                    DateTimeOffset.FromUnixTimeMilliseconds(
                        BinaryPrimitives.ReadInt64BigEndian(Slice(block, ref offset, 8))
                    )
                );
            case HeaderType.Uuid:
                return new EventStreamHeaderValue.Uuid(
                    new Guid(Slice(block, ref offset, 16), bigEndian: true)
                );
            default:
                throw new InvalidDataException(
                    $"Event stream header '{name}' has unknown value type {(byte)type}."
                );
        }
    }

    private static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> block, ref int offset, int length)
    {
        if (offset + length > block.Length)
        {
            throw new InvalidDataException("Truncated event stream header block.");
        }

        var slice = block.Slice(offset, length);
        offset += length;
        return slice;
    }

    private enum HeaderType : byte
    {
        BoolTrue = 0,
        BoolFalse = 1,
        Byte = 2,
        Int16 = 3,
        Int32 = 4,
        Int64 = 5,
        ByteArray = 6,
        String = 7,
        Timestamp = 8,
        Uuid = 9,
    }
}
