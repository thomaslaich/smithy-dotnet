using NSmithy.Http;

namespace NSmithy.Client;

public sealed class SmithyOperationInvoker(
    IHttpTransport transport,
    IEnumerable<ISmithyClientMiddleware>? middlewares = null
)
{
    private readonly SmithyClientRuntime runtime = new(transport, middlewares: middlewares);

    public Task<SmithyHttpResponse> InvokeAsync(
        string serviceName,
        string operationName,
        SmithyHttpRequest request,
        SmithyErrorDeserializer? errorDeserializer = null,
        Func<SmithyHttpResponse, bool>? isErrorResponse = null,
        CancellationToken cancellationToken = default
    ) =>
        runtime.InvokeAsync(
            serviceName,
            operationName,
            request,
            errorDeserializer,
            isErrorResponse,
            cancellationToken
        );
}
