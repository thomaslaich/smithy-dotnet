namespace NSmithy.Http;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1054:URI-like parameters should not be strings",
    Justification = "SmithyHttpRequest preserves protocol-relative request URI text before endpoint resolution."
)]
public sealed class SmithyHttpRequest(HttpMethod method, string requestUri)
{
    private string requestUri = requestUri ?? throw new ArgumentNullException(nameof(requestUri));
    private Dictionary<string, IReadOnlyList<string>>? contentHeaders;

    public HttpMethod Method { get; } = method ?? throw new ArgumentNullException(nameof(method));

    public string RequestUri
    {
        get => requestUri;
        set => requestUri = value ?? throw new ArgumentNullException(nameof(value));
    }

    public IDictionary<string, IReadOnlyList<string>> Headers { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    public SmithyHttpBody Body { get; set; } = SmithyHttpBody.Empty;

    public bool ExpectStreamingResponse { get; set; }

    public string? ContentType { get; set; }

    public IDictionary<string, IReadOnlyList<string>> ContentHeaders =>
        contentHeaders ??= new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase
        );

    internal Dictionary<string, IReadOnlyList<string>>? ExistingContentHeaders => contentHeaders;
}
