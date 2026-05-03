using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;

namespace RpcV2Cbor.Conformance;

/// <summary>
/// HTTP handler that records the outgoing request (method, URI, headers, body) and returns a
/// canned response. Used by the protocol conformance harness to inspect what the generated
/// client emitted on the wire without involving any network IO.
/// </summary>
internal sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> respond;

    public RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        this.respond = respond;
    }

    public RecordedRequest? Captured { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var headers = new ConcurrentDictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var h in request.Headers)
        {
            headers[h.Key] = [.. h.Value];
        }
        byte[] body = [];
        string? contentType = null;
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            foreach (var h in request.Content.Headers)
            {
                headers[h.Key] = [.. h.Value];
            }
            contentType = request.Content.Headers.ContentType?.ToString();
        }

        Captured = new RecordedRequest(
            request.Method.Method,
            request.RequestUri ?? throw new InvalidOperationException("Request URI is null."),
            headers,
            body,
            contentType
        );
        return respond(request);
    }

    public static HttpResponseMessage EmptyOk() =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
}

internal sealed record RecordedRequest(
    string Method,
    Uri RequestUri,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
    byte[] Body,
    string? ContentType
);
