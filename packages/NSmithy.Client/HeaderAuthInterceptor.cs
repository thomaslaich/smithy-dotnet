using NSmithy.Http;

namespace NSmithy.Client;

/// <summary>Installs a single request header carrying a credential.</summary>
internal sealed class HeaderAuthInterceptor(string headerName, string headerValue)
    : ISmithyAuthHandler
{
    private readonly string headerName = string.IsNullOrWhiteSpace(headerName)
        ? throw new ArgumentException("Header name must be set.", nameof(headerName))
        : headerName;

    private readonly string headerValue =
        headerValue ?? throw new ArgumentNullException(nameof(headerValue));

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
