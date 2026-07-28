using System.Runtime.CompilerServices;

namespace NSmithy.Http;

/// <summary>
/// A protocol-neutral server response: the server half of an operation protocol produces one and a
/// host adapter writes it. Carries every response shape — a unary body, a streamed event body, and
/// protocol trailers — behind one type, so the host writes them uniformly and holds no wire
/// knowledge of any protocol.
/// </summary>
public sealed class SmithyHttpServerResponse
{
    public int StatusCode { get; init; } = 200;

    public IDictionary<string, IReadOnlyList<string>> Headers { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The response body as write-ready chunks: a unary response is one chunk, a streamed event
    /// response is many. The protocol owns all framing.
    /// </summary>
    public IAsyncEnumerable<ReadOnlyMemory<byte>> Body { get; init; } =
        AsyncEnumerable.Empty<ReadOnlyMemory<byte>>();

    /// <summary>
    /// The body's byte length when known (unary responses), so the host can emit Content-Length.
    /// Null for streamed responses.
    /// </summary>
    public long? ContentLength { get; init; }

    /// <summary>
    /// Trailer content, evaluated after the body completes. The argument is the streaming error
    /// (null on clean completion) so a protocol can reflect a mid-stream failure into its trailers
    /// (e.g. gRPC's <c>grpc-status</c>). Null when the protocol emits no trailers. Whether the
    /// connection can carry trailers at all is the host's decision, not the protocol's.
    /// </summary>
    public Func<Exception?, IReadOnlyList<KeyValuePair<string, string>>>? Trailers { get; init; }

    /// <summary>Creates a unary response from a single buffered body chunk.</summary>
    public static SmithyHttpServerResponse Unary(
        int statusCode,
        ReadOnlyMemory<byte> body,
        Action<IDictionary<string, IReadOnlyList<string>>>? headers = null
    )
    {
        var response = new SmithyHttpServerResponse
        {
            StatusCode = statusCode,
            Body = SingleChunk(body),
            ContentLength = body.Length,
        };
        headers?.Invoke(response.Headers);
        return response;
    }

    /// <summary>
    /// Adapts a <see cref="SmithyHttpClientResponse"/> into a server response. A protocol whose unary
    /// serialization already produces a <see cref="SmithyHttpClientResponse"/> converts at the boundary:
    /// the two header collections merge (a response is written to one header set), and the body
    /// becomes write-ready chunks with a known length for buffered bodies.
    /// </summary>
    public static SmithyHttpServerResponse FromHttp(SmithyHttpClientResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var (body, length) = ToChunks(response.Body);
        var result = new SmithyHttpServerResponse
        {
            StatusCode = (int)response.StatusCode,
            Body = body,
            ContentLength = length,
        };
        foreach (var header in response.Headers)
        {
            result.Headers[header.Key] = header.Value;
        }

        foreach (var header in response.ContentHeaders)
        {
            result.Headers[header.Key] = header.Value;
        }

        return result;
    }

    private static (IAsyncEnumerable<ReadOnlyMemory<byte>> Body, long? Length) ToChunks(
        SmithyHttpBody body
    ) =>
        body switch
        {
            SmithyHttpBody.Bytes bytes => (SingleChunk(bytes.Content), bytes.Content.Length),
            SmithyHttpBody.Streaming streaming => (
                ReadStream(streaming.Content),
                streaming.ContentLength
            ),
            _ => (AsyncEnumerable.Empty<ReadOnlyMemory<byte>>(), 0L),
        };

    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> SingleChunk(
        ReadOnlyMemory<byte> chunk
    )
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield return chunk;
    }

    // The buffer is reused across iterations: valid because the host writer consumes each chunk
    // (writes and flushes it) before pulling the next, exactly like Stream.CopyToAsync.
    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadStream(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            yield return buffer.AsMemory(0, read);
        }
    }
}
