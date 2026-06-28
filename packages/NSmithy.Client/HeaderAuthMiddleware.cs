using NSmithy.Http;

namespace NSmithy.Client;

/// <summary>Installs a single request header carrying a credential, then calls the next middleware.</summary>
internal sealed class HeaderAuthMiddleware(string headerName, string headerValue)
    : ISmithyAuthHandler
{
    private readonly string headerName = string.IsNullOrWhiteSpace(headerName)
        ? throw new ArgumentException("Header name must be set.", nameof(headerName))
        : headerName;

    private readonly string headerValue =
        headerValue ?? throw new ArgumentNullException(nameof(headerValue));

    public Task<SmithyOperationResponse> InvokeAsync(
        SmithyOperationRequest request,
        SmithyOperationNext nextOperation,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(nextOperation);

        request.Request.Headers[headerName] = [headerValue];
        return nextOperation(request, cancellationToken);
    }

    public ValueTask<SmithyHttpRequest> OnBeforeTransmitAsync(
        SmithyContext context,
        SmithyHttpRequest request,
        CancellationToken cancellationToken = default
    )
    {
        request.Headers[headerName] = [headerValue];
        return ValueTask.FromResult(request);
    }
}
