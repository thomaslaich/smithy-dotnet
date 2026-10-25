using System.Net;

namespace NSmithy.Client;

/// <summary>
/// An error response that matched none of the operation's modeled errors.
/// </summary>
public sealed class SmithyClientException(
    HttpStatusCode statusCode,
    string? reasonPhrase,
    string? errorCode = null
)
    : Exception(
        errorCode is null
            ? $"Smithy service returned HTTP {(int)statusCode} {reasonPhrase}".TrimEnd()
            : $"Smithy service returned HTTP {(int)statusCode} with error {errorCode}"
    )
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public string? ReasonPhrase { get; } = reasonPhrase;

    /// <summary>
    /// The error's name as the protocol reports it, without a namespace, such as
    /// <c>InvalidInstanceID.NotFound</c>. Null when the response names no error.
    /// </summary>
    public string? ErrorCode { get; } = errorCode;
}
