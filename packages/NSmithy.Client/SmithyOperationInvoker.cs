using NSmithy.Http;

namespace NSmithy.Client;

public sealed class SmithyOperationInvoker(
    IHttpTransport transport,
    IEnumerable<ISmithyClientMiddleware>? middlewares = null
)
{
    private readonly IHttpTransport transport =
        transport ?? throw new ArgumentNullException(nameof(transport));
    private readonly IReadOnlyList<ISmithyClientMiddleware> middlewares = [.. middlewares ?? []];

    public async Task<SmithyHttpResponse> InvokeAsync(
        string serviceName,
        string operationName,
        SmithyHttpRequest request,
        SmithyErrorDeserializer? errorDeserializer = null,
        Func<SmithyHttpResponse, bool>? isErrorResponse = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentNullException.ThrowIfNull(request);

        var operationRequest = new SmithyOperationRequest(serviceName, operationName, request);
        var operationResponse = await BuildPipeline(0)
            .Invoke(operationRequest, cancellationToken)
            .ConfigureAwait(false);
        var response = operationResponse.Response;

        // Whether a response is an error is a protocol decision (HTTP status for REST/rpcv2Cbor,
        // the grpc-status trailer for gRPC), supplied by the caller. The default preserves the
        // HTTP convention for callers that don't provide one.
        var isError = isErrorResponse?.Invoke(response) ?? (int)response.StatusCode >= 400;
        if (!isError)
        {
            return response;
        }

        if (errorDeserializer is not null)
        {
            var error = await errorDeserializer(response, cancellationToken).ConfigureAwait(false);
            if (error is not null)
            {
                throw error;
            }
        }

        throw new SmithyClientException(response.StatusCode, response.ReasonPhrase);
    }

    private SmithyOperationNext BuildPipeline(int index)
    {
        if (index >= middlewares.Count)
        {
            return async (request, cancellationToken) =>
            {
                var response = await transport
                    .SendAsync(request.Request, cancellationToken)
                    .ConfigureAwait(false);
                return new SmithyOperationResponse(
                    request.ServiceName,
                    request.OperationName,
                    response
                );
            };
        }

        var current = middlewares[index];
        var next = BuildPipeline(index + 1);
        return (request, cancellationToken) =>
            current.InvokeAsync(request, next, cancellationToken);
    }
}
