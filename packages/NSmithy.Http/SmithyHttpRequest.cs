namespace NSmithy.Http;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1054:URI-like parameters should not be strings",
    Justification = "SmithyHttpRequest preserves protocol-relative request URI text before endpoint resolution."
)]
public sealed class SmithyHttpRequest(HttpMethod method, string requestUri)
{
    public HttpMethod Method { get; } = method ?? throw new ArgumentNullException(nameof(method));

    public string RequestUri { get; } =
        requestUri ?? throw new ArgumentNullException(nameof(requestUri));

    public IDictionary<string, IReadOnlyList<string>> Headers { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    public SmithyHttpBody Body { get; set; } = SmithyHttpBody.Empty;

    public bool ExpectStreamingResponse { get; set; }

    public string? ContentType { get; set; }

    public IDictionary<string, IReadOnlyList<string>> ContentHeaders { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
}
