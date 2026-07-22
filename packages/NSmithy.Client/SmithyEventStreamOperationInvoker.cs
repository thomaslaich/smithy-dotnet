using NSmithy.Http;

namespace NSmithy.Client;

/// <summary>
/// Sends event-stream operation requests over a streaming transport. The protocol produces the
/// framed request body and deframes the response stream; this invoker owns only the send.
/// </summary>
public sealed class SmithyEventStreamOperationInvoker(IStreamingHttpTransport transport)
{
    private readonly IStreamingHttpTransport transport =
        transport ?? throw new ArgumentNullException(nameof(transport));

    public Task<SmithyStreamingHttpResponse> InvokeAsync(
        string serviceName,
        string operationName,
        SmithyHttpRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentNullException.ThrowIfNull(request);

        return transport.SendAsync(request, cancellationToken);
    }
}
