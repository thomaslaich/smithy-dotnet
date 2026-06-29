using NSmithy.Http;

namespace NSmithy.Client;

/// <summary>
/// Appends a credential as a query-string parameter on the request URI, then calls the next
/// middleware. The request URI is immutable, so this rebuilds the HTTP request, carrying over
/// method, body and headers.
/// </summary>
internal sealed class QueryParameterAuthMiddleware(string name, string value) : ISmithyAuthHandler
{
    private readonly string name = string.IsNullOrWhiteSpace(name)
        ? throw new ArgumentException("Query parameter name must be set.", nameof(name))
        : name;

    private readonly string value = value ?? throw new ArgumentNullException(nameof(value));

    public Task<SmithyOperationResponse> InvokeAsync(
        SmithyOperationRequest request,
        SmithyOperationNext nextOperation,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(nextOperation);

        var signed = AddQueryParameter(request.Request);
        return nextOperation(request with { Request = signed }, cancellationToken);
    }

    public ValueTask<SmithyHttpRequest> OnBeforeTransmitAsync(
        SmithyContext context,
        SmithyHttpRequest request,
        CancellationToken cancellationToken = default
    ) => ValueTask.FromResult(AddQueryParameter(request));

    private SmithyHttpRequest AddQueryParameter(SmithyHttpRequest original)
    {
        var separator = original.RequestUri.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var requestUri =
            $"{original.RequestUri}{separator}{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}";

        var signed = new SmithyHttpRequest(original.Method, requestUri)
        {
            Content = original.Content,
            ContentType = original.ContentType,
            ExpectStreamingResponse = original.ExpectStreamingResponse,
            StreamingContent = original.StreamingContent,
            StreamingContentLength = original.StreamingContentLength,
        };
        foreach (var header in original.Headers)
        {
            signed.Headers[header.Key] = header.Value;
        }

        foreach (var header in original.ContentHeaders)
        {
            signed.ContentHeaders[header.Key] = header.Value;
        }

        return signed;
    }
}
