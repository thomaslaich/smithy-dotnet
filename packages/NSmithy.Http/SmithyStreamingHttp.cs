using System.Net;

namespace NSmithy.Http;

/// <summary>
/// An HTTP response whose body streams in. The protocol deframes <see cref="Body"/> itself;
/// <see cref="Trailer"/> resolves HTTP trailing headers (falling back to response headers) and
/// is meaningful only after the body has been read to its end. Disposing <see cref="Body"/>
/// releases the underlying connection. Distinct from <see cref="SmithyHttpResponse"/> because
/// incremental read, connection-hold-until-dispose, and trailer-after-EOF are behavioral
/// properties a buffered unary response does not have.
/// </summary>
public sealed record SmithyStreamingHttpResponse(
    HttpStatusCode StatusCode,
    string? ReasonPhrase,
    Stream Body,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ContentHeaders,
    Func<string, string?>? Trailer = null
);

/// <summary>
/// A streaming HTTP transport: sends a request whose body may stream out (a
/// <see cref="SmithyHttpBody.EventStreaming"/> body) and returns a response whose body streams in.
/// Protocol-neutral — one implementation serves every streaming protocol because framing lives in
/// the protocols. The request and response streaming axes are independent, so the request is an
/// ordinary <see cref="SmithyHttpRequest"/> whose body says whether it streams.
/// </summary>
public interface IStreamingHttpTransport
{
    Task<SmithyStreamingHttpResponse> SendAsync(
        SmithyHttpRequest request,
        CancellationToken cancellationToken = default
    );
}
