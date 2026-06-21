using System.Buffers.Binary;

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

        var length = (int)BinaryPrimitives.ReadUInt32BigEndian(body.Slice(1, 4));
        if (body.Length < HeaderLength + length)
        {
            throw new InvalidOperationException("Truncated gRPC frame payload.");
        }

        return body.Slice(HeaderLength, length).ToArray();
    }
}
