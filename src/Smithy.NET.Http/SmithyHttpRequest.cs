namespace Smithy.NET.Http;

public sealed class SmithyHttpRequest(HttpMethod method, string requestUri)
{
    public HttpMethod Method { get; } = method ?? throw new ArgumentNullException(nameof(method));

    public string RequestUri { get; } =
        requestUri ?? throw new ArgumentNullException(nameof(requestUri));

    public IDictionary<string, IReadOnlyList<string>> Headers { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    public string? Content { get; set; }

    public string? ContentType { get; set; }

    public IDictionary<string, IReadOnlyList<string>> ContentHeaders { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
}
