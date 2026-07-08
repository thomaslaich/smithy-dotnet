using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace NSmithy.Protocols.Grpc;

/// <summary>
/// The gRPC length-prefixed message framing. Every gRPC message on the wire is a 5-byte header — a
/// 1-byte compression flag followed by a 4-byte big-endian message length — and then that many
/// payload bytes. NSmithy emits and consumes this framing directly rather than relying on
/// Grpc.Core / Grpc.Net, so the proto payload produced by <c>ProtoCodec</c> goes straight onto the
/// HTTP/2 body.
/// </summary>
public static class GrpcMessageFraming
{
    public const int HeaderLength = 5;

    /// <summary>Wraps a single proto message payload in an uncompressed gRPC frame.</summary>
    public static byte[] Frame(ReadOnlySpan<byte> payload)
    {
        var frame = new byte[HeaderLength + payload.Length];
        frame[0] = 0; // compression flag: 0 = uncompressed
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(1, 4), (uint)payload.Length);
        payload.CopyTo(frame.AsSpan(HeaderLength));
        return frame;
    }

    /// <summary>
    /// Reads the first message from a gRPC body. Unary calls carry exactly one frame; this returns
    /// its payload, or an empty array when the body is empty (e.g. a <c>google.protobuf.Empty</c>
    /// request/response).
    /// </summary>
    public static byte[] ReadSingle(ReadOnlySpan<byte> body)
    {
        if (body.Length == 0)
        {
            return [];
        }

        if (body.Length < HeaderLength)
        {
            throw new InvalidOperationException("Truncated gRPC frame header.");
        }

        var compressed = body[0];
        if (compressed != 0)
        {
            throw new NotSupportedException(
                "Compressed gRPC messages are not yet supported by NSmithy."
            );
        }

        var length = ReadFrameLength(body.Slice(1, 4));
        if (body.Length < HeaderLength + length)
        {
            throw new InvalidOperationException("Truncated gRPC frame payload.");
        }

        return body.Slice(HeaderLength, length).ToArray();
    }

    public static async ValueTask WriteAsync(
        Stream stream,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[HeaderLength];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(1, 4), (uint)payload.Length);

        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        if (payload.Length > 0)
        {
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }
    }

    public static async IAsyncEnumerable<byte[]> ReadAllAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[HeaderLength];
        while (await TryReadExactAsync(stream, header, cancellationToken).ConfigureAwait(false))
        {
            if (header[0] != 0)
            {
                throw new NotSupportedException(
                    "Compressed gRPC messages are not yet supported by NSmithy."
                );
            }

            var length = ReadFrameLength(header.AsSpan(1, 4));
            var payload = new byte[length];
            if (
                length > 0
                && !await TryReadExactAsync(stream, payload, cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                throw new InvalidOperationException("Truncated gRPC frame payload.");
            }

            yield return payload;
        }
    }

    /// <summary>
    /// Reads the 4-byte big-endian frame length and guards against the value overflowing
    /// <see cref="int"/> — a frame header declaring more than <see cref="int.MaxValue"/> bytes
    /// (e.g. from a buggy or hostile peer) would otherwise wrap negative and crash the allocation.
    /// </summary>
    public static int ReadFrameLength(ReadOnlySpan<byte> lengthBytes)
    {
        var length = BinaryPrimitives.ReadUInt32BigEndian(lengthBytes);
        if (length > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"gRPC frame length {length} exceeds the maximum supported size of {int.MaxValue} bytes."
            );
        }

        return (int)length;
    }

    private static async ValueTask<bool> TryReadExactAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken
    )
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream
                .ReadAsync(buffer[read..], cancellationToken)
                .ConfigureAwait(false);
            if (count == 0)
            {
                if (read == 0)
                {
                    return false;
                }

                throw new InvalidOperationException("Truncated gRPC frame header.");
            }

            read += count;
        }

        return true;
    }
}
