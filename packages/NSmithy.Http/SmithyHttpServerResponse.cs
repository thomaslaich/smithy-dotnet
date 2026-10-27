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

    /// <summary>
    /// A body of one chunk holding the whole response, which a host can recognize with
    /// <see cref="TryGetSingleChunk"/> and write in one call.
    /// </summary>
    public static IAsyncEnumerable<ReadOnlyMemory<byte>> SingleChunk(
        ReadOnlyMemory<byte> content
    ) => new SingleChunkBody(content);

    /// <summary>The whole body, when it was made with <see cref="SingleChunk"/>.</summary>
    public bool TryGetSingleChunk(out ReadOnlyMemory<byte> content)
    {
        if (Body is SingleChunkBody single)
        {
            content = single.Content;
            return true;
        }

        content = default;
        return false;
    }

    private sealed class SingleChunkBody(ReadOnlyMemory<byte> content)
        : IAsyncEnumerable<ReadOnlyMemory<byte>>
    {
        public ReadOnlyMemory<byte> Content { get; } = content;

        public IAsyncEnumerator<ReadOnlyMemory<byte>> GetAsyncEnumerator(
            CancellationToken cancellationToken = default
        ) => Enumerate().GetAsyncEnumerator(cancellationToken);

        private async IAsyncEnumerable<ReadOnlyMemory<byte>> Enumerate()
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return Content;
        }
    }
}
