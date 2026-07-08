using NSmithy.Http;

namespace NSmithy.Client;

/// <summary>
/// Sends event-stream operation requests over a duplex transport. The protocol produces the
/// framed request body and deframes the response stream; this invoker owns only the send.
/// </summary>
public sealed class SmithyEventStreamOperationInvoker(IDuplexHttpTransport transport)
{
    private readonly IDuplexHttpTransport transport =
        transport ?? throw new ArgumentNullException(nameof(transport));

    public Task<SmithyDuplexHttpResponse> InvokeAsync(
        string serviceName,
        string operationName,
        SmithyDuplexHttpRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentNullException.ThrowIfNull(request);

        return transport.SendAsync(request, cancellationToken);
    }
}
