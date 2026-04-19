using System.Net;

namespace Smithy.NET.Client;

public sealed class SmithyClientException(HttpStatusCode statusCode, string? reasonPhrase)
    : Exception($"Smithy service returned HTTP {(int)statusCode} {reasonPhrase}".TrimEnd())
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public string? ReasonPhrase { get; } = reasonPhrase;
}
