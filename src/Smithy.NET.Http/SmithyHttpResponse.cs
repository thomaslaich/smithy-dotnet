using System.Net;

namespace Smithy.NET.Http;

public sealed record SmithyHttpResponse(
    HttpStatusCode StatusCode,
    string? ReasonPhrase,
    string Content,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ContentHeaders
)
{
    public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and <= 299;
}
