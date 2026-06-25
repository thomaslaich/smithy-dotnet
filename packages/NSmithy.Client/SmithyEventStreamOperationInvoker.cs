using NSmithy.Http;

namespace NSmithy.Client;

public sealed class SmithyEventStreamOperationInvoker(IEventStreamHttpTransport transport)
{
    private readonly IEventStreamHttpTransport transport =
        transport ?? throw new ArgumentNullException(nameof(transport));

    public Task<SmithyEventStreamHttpResponse> InvokeAsync(
        string serviceName,
        string operationName,
        SmithyEventStreamHttpRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentNullException.ThrowIfNull(request);

        return transport.SendAsync(request, cancellationToken);
    }
}
