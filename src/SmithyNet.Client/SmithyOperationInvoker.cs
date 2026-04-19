using SmithyNet.Http;

namespace SmithyNet.Client;

public sealed class SmithyOperationInvoker(
    IHttpTransport transport,
    IEnumerable<ISmithyClientMiddleware>? middleware = null
)
{
    private readonly IHttpTransport transport =
        transport ?? throw new ArgumentNullException(nameof(transport));
    private readonly IReadOnlyList<ISmithyClientMiddleware> middleware = [.. middleware ?? []];

    public async Task<SmithyHttpResponse> InvokeAsync(
        string serviceName,
        string operationName,
        SmithyHttpRequest request,
        SmithyErrorDeserializer? errorDeserializer = null,
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

        if (operationResponse.Response.IsSuccessStatusCode)
        {
            return operationResponse.Response;
        }

        if (errorDeserializer is not null)
        {
            var error = await errorDeserializer(operationResponse.Response, cancellationToken)
                .ConfigureAwait(false);
            if (error is not null)
            {
                throw error;
            }
        }

        throw new SmithyClientException(
            operationResponse.Response.StatusCode,
            operationResponse.Response.ReasonPhrase
        );
    }

    private SmithyOperationNext BuildPipeline(int index)
    {
        if (index >= middleware.Count)
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

        var current = middleware[index];
        var next = BuildPipeline(index + 1);
        return (request, cancellationToken) =>
            current.InvokeAsync(request, next, cancellationToken);
    }
}
