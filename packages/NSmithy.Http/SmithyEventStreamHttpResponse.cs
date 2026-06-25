using System.Net;

namespace NSmithy.Http;

public sealed record SmithyEventStreamHttpResponse(
    HttpStatusCode StatusCode,
    string? ReasonPhrase,
    IAsyncEnumerable<SmithyEventFrame> Events,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ContentHeaders
);
