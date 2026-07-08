using System.Net;

namespace NSmithy.Http;

/// <summary>
/// An HTTP request whose body is produced incrementally as protocol-framed byte chunks. The
/// protocol owns all wire framing (gRPC length prefixes, vnd.amazon.eventstream envelopes); the
/// transport writes each chunk and flushes, nothing more.
/// </summary>
public sealed class SmithyDuplexHttpRequest(HttpMethod method, string requestUri)
{
    public HttpMethod Method { get; } = method ?? throw new ArgumentNullException(nameof(method));

    public string RequestUri { get; } =
        requestUri ?? throw new ArgumentNullException(nameof(requestUri));

    public IDictionary<string, IReadOnlyList<string>> Headers { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Protocol-framed body chunks; each chunk is written and flushed as one unit.</summary>
    public IAsyncEnumerable<ReadOnlyMemory<byte>> Body { get; set; } =
        AsyncEnumerable.Empty<ReadOnlyMemory<byte>>();

    public string? ContentType { get; set; }

    public IDictionary<string, IReadOnlyList<string>> ContentHeaders { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// An HTTP response whose body streams. The protocol deframes <see cref="Body"/> itself;
/// <see cref="Trailer"/> resolves HTTP trailing headers (falling back to response headers) and
/// is meaningful only after the body has been read to its end. Disposing <see cref="Body"/>
/// releases the underlying connection.
/// </summary>
public sealed record SmithyDuplexHttpResponse(
    HttpStatusCode StatusCode,
    string? ReasonPhrase,
    Stream Body,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ContentHeaders,
    Func<string, string?>? Trailer = null
);

/// <summary>
/// A duplex HTTP transport: sends a request whose body streams out and returns a response whose
/// body streams in. Protocol-neutral — one implementation serves every streaming protocol
/// because framing lives in the protocols.
/// </summary>
public interface IDuplexHttpTransport
{
    Task<SmithyDuplexHttpResponse> SendAsync(
        SmithyDuplexHttpRequest request,
        CancellationToken cancellationToken = default
    );
}
