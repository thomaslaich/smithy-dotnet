using NSmithy.Http;

namespace NSmithy.Client;

public sealed class SmithyClientRuntime(
    IHttpTransport transport,
    IEnumerable<IClientInterceptor>? interceptors = null,
    ISmithyRetryStrategy? retryStrategy = null
)
{
    private readonly IHttpTransport transport =
        transport ?? throw new ArgumentNullException(nameof(transport));
    private readonly IReadOnlyList<IClientInterceptor> interceptors = [.. interceptors ?? []];
    private readonly ISmithyRetryStrategy? retryStrategy = retryStrategy;

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
        for (var attempt = 1; ; attempt++)
        {
            context.Set(SmithyContextKeys.Attempt, attempt);
            var attemptRequest = await ApplyRequestInterceptorsAsync(
                    context,
                    CloneRequest(request),
                    cancellationToken
                )
                .ConfigureAwait(false);

            var response = await transport
                .SendAsync(attemptRequest, cancellationToken)
                .ConfigureAwait(false);

            for (var i = interceptors.Count - 1; i >= 0; i--)
            {
                interceptors[i].OnAfterTransmit(context, response);
            }

            var retryContext = new SmithyRetryContext(attempt, response, context);
            if (
                retryStrategy is not null
                && attempt < retryStrategy.MaxAttempts
                && request.StreamingContent is null
                && retryStrategy.ShouldRetry(retryContext)
            )
            {
                await retryStrategy
                    .DelayAsync(retryContext, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            var isError = isErrorResponse?.Invoke(response) ?? (int)response.StatusCode >= 400;
            if (!isError)
            {
                return response;
            }

            if (errorDeserializer is not null)
            {
                var error = await errorDeserializer(response, cancellationToken)
                    .ConfigureAwait(false);
                if (error is not null)
                {
                    throw error;
                }
            }

            throw new SmithyClientException(response.StatusCode, response.ReasonPhrase);
        }
    }

    private async Task<SmithyHttpRequest> ApplyRequestInterceptorsAsync(
        SmithyContext context,
        SmithyHttpRequest request,
        CancellationToken cancellationToken
    )
    {
        foreach (var interceptor in interceptors)
        {
            request = await interceptor
                .OnBeforeSigningAsync(context, request, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var interceptor in interceptors)
        {
            request = await interceptor
                .OnBeforeTransmitAsync(context, request, cancellationToken)
                .ConfigureAwait(false);
        }

        return request;
    }

    private static SmithyHttpRequest CloneRequest(SmithyHttpRequest request)
    {
        var clone = new SmithyHttpRequest(request.Method, request.RequestUri)
        {
            Content = request.Content,
            StreamingContent = request.StreamingContent,
            StreamingContentLength = request.StreamingContentLength,
            ExpectStreamingResponse = request.ExpectStreamingResponse,
            ContentType = request.ContentType,
        };

        foreach (var header in request.Headers)
        {
            clone.Headers[header.Key] = header.Value;
        }

        foreach (var header in request.ContentHeaders)
        {
            clone.ContentHeaders[header.Key] = header.Value;
        }

        return clone;
    }

    private static SmithyContext CreateContext(string serviceName, string operationName)
    {
        var context = new SmithyContext();
        context.Set(SmithyContextKeys.ServiceName, serviceName);
        context.Set(SmithyContextKeys.OperationName, operationName);
        return context;
    }
}
