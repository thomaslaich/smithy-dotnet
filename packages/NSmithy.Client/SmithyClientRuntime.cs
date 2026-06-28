using NSmithy.Http;

namespace NSmithy.Client;

public sealed class SmithyClientRuntime(
    IHttpTransport transport,
    IEnumerable<IClientInterceptor>? interceptors = null,
    IEnumerable<ISmithyClientMiddleware>? middlewares = null
)
{
    private readonly IHttpTransport transport =
        transport ?? throw new ArgumentNullException(nameof(transport));
    private readonly IReadOnlyList<IClientInterceptor> interceptors = [.. interceptors ?? []];
    private readonly IReadOnlyList<ISmithyClientMiddleware> middlewares = [.. middlewares ?? []];

    public async Task<TOutput> InvokeAsync<TInput, TOutput>(
        string serviceName,
        string operationName,
        IOperationProtocol<TInput, TOutput> protocol,
        TInput input,
        Action<SmithyHttpRequest>? modifyRequest = null,
        SmithyErrorDeserializer? errorDeserializer = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentNullException.ThrowIfNull(protocol);

        var context = CreateContext(serviceName, operationName);
        foreach (var interceptor in interceptors)
        {
            interceptor.OnBeforeExecution(context);
        }

        try
        {
            foreach (var interceptor in interceptors)
            {
                interceptor.OnBeforeSerialization(context, input);
            }

            var request = protocol.SerializeRequest(input);
            modifyRequest?.Invoke(request);
            var response = await SendAsync(
                    context,
                    serviceName,
                    operationName,
                    request,
                    errorDeserializer,
                    protocol.IsErrorResponse,
                    cancellationToken
                )
                .ConfigureAwait(false);

            var output = protocol.DeserializeResponse(response);
            for (var i = interceptors.Count - 1; i >= 0; i--)
            {
                interceptors[i].OnAfterDeserialization(context, output);
            }

            return output;
        }
        finally
        {
            for (var i = interceptors.Count - 1; i >= 0; i--)
            {
                interceptors[i].OnAfterExecution(context);
            }
        }
    }

    public Task<SmithyHttpResponse> InvokeAsync(
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

        var context = CreateContext(serviceName, operationName);
        foreach (var interceptor in interceptors)
        {
            interceptor.OnBeforeExecution(context);
        }

        return InvokeWithCompletionAsync();

        async Task<SmithyHttpResponse> InvokeWithCompletionAsync()
        {
            try
            {
                return await SendAsync(
                        context,
                        serviceName,
                        operationName,
                        request,
                        errorDeserializer,
                        isErrorResponse,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            finally
            {
                for (var i = interceptors.Count - 1; i >= 0; i--)
                {
                    interceptors[i].OnAfterExecution(context);
                }
            }
        }
    }

    private async Task<SmithyHttpResponse> SendAsync(
        SmithyContext context,
        string serviceName,
        string operationName,
        SmithyHttpRequest request,
        SmithyErrorDeserializer? errorDeserializer,
        Func<SmithyHttpResponse, bool>? isErrorResponse,
        CancellationToken cancellationToken
    )
    {
        foreach (var interceptor in interceptors)
        {
            request = interceptor.OnBeforeSigning(context, request);
        }

        foreach (var interceptor in interceptors)
        {
            request = interceptor.OnBeforeTransmit(context, request);
        }

        var operationRequest = new SmithyOperationRequest(serviceName, operationName, request);
        var operationResponse = await BuildPipeline(0)
            .Invoke(operationRequest, cancellationToken)
            .ConfigureAwait(false);
        var response = operationResponse.Response;

        for (var i = interceptors.Count - 1; i >= 0; i--)
        {
            interceptors[i].OnAfterTransmit(context, response);
        }

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

    private static SmithyContext CreateContext(string serviceName, string operationName)
    {
        var context = new SmithyContext();
        context.Set(SmithyContextKeys.ServiceName, serviceName);
        context.Set(SmithyContextKeys.OperationName, operationName);
        return context;
    }
}
