using System.Net;

namespace Bench.Clients;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that answers from canned bytes instead of
/// a server.
/// </summary>
/// <remarks>
/// The measurement surface for the client suite: with no server in the loop, what
/// remains is the client's own work. A real server would contribute a large shared
/// constant that compresses every ratio. The handler reads the request body to
/// completion on purpose, a client that defers serialization would otherwise
/// never pay for it.
/// </remarks>
public sealed class StubTransport : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, StubResponse> resolve;
    private readonly List<CapturedRequest>? captures;

    /// <param name="resolve">Chooses the canned response for a request.</param>
    /// <param name="record">
    /// When true, every request is recorded for the parity gate. Leave false when
    /// benchmarking, recording allocates and would land in the numbers.
    /// </param>
    public StubTransport(Func<HttpRequestMessage, StubResponse> resolve, bool record = false)
    {
        this.resolve = resolve;
        captures = record ? [] : null;
    }

    /// <summary>Requests seen so far, when recording is enabled.</summary>
    public IReadOnlyList<CapturedRequest> Captures =>
        captures ?? throw new InvalidOperationException("This transport was not recording.");

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var body = request.Content is null
            ? []
            : await request.Content.ReadAsByteArrayAsync(cancellationToken);

        if (captures is not null)
            captures.Add(CapturedRequest.From(request, body));

        var canned = resolve(request);
        var response = new HttpResponseMessage(canned.Status)
        {
            RequestMessage = request,
            Content = new ByteArrayContent(canned.Body),
        };

        response.Content.Headers.TryAddWithoutValidation("Content-Type", canned.ContentType);
        foreach (var (name, value) in canned.Headers)
            response.Headers.TryAddWithoutValidation(name, value);

        return response;
    }

    protected override void Dispose(bool disposing)
    {
        captures?.Clear();
        base.Dispose(disposing);
    }
}

/// <summary>A canned response.</summary>
public sealed record StubResponse(
    HttpStatusCode Status,
    byte[] Body,
    string ContentType = "application/json",
    IReadOnlyList<(string Name, string Value)>? ExtraHeaders = null
)
{
    public IReadOnlyList<(string Name, string Value)> Headers => ExtraHeaders ?? [];
}
