namespace SmithyNet.Server;

public sealed class SmithyServerDispatcher(IEnumerable<ISmithyServerMiddleware>? middleware = null)
{
    private readonly Dictionary<OperationKey, SmithyServerOperationHandler> handlers = [];
    private readonly IReadOnlyList<ISmithyServerMiddleware> middleware = [.. middleware ?? []];

    public void Register(
        string serviceName,
        string operationName,
        SmithyServerOperationHandler handler
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentNullException.ThrowIfNull(handler);

        handlers[new OperationKey(serviceName, operationName)] = handler;
    }

    public Task<SmithyServerResponse> DispatchAsync(
        SmithyServerRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        return BuildPipeline(0).Invoke(request, cancellationToken);
    }

    private SmithyServerOperationNext BuildPipeline(int index)
    {
        if (index >= middleware.Count)
        {
            return InvokeHandlerAsync;
        }

        var current = middleware[index];
        var next = BuildPipeline(index + 1);
        return (request, cancellationToken) =>
            current.InvokeAsync(request, next, cancellationToken);
    }

    private Task<SmithyServerResponse> InvokeHandlerAsync(
        SmithyServerRequest request,
        CancellationToken cancellationToken
    )
    {
        var key = new OperationKey(request.ServiceName, request.OperationName);
        if (!handlers.TryGetValue(key, out var handler))
        {
            throw new SmithyServerOperationNotFoundException(
                request.ServiceName,
                request.OperationName
            );
        }

        return handler(request, cancellationToken);
    }

    private readonly record struct OperationKey(string ServiceName, string OperationName);
}
