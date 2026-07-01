using NSmithy.Http;

namespace NSmithy.Client;

public sealed class SmithyClientRuntime(
    IHttpTransport transport,
    IEnumerable<IClientInterceptor>? interceptors = null,
    ISmithyRetryStrategy? retryStrategy = null,
    Uri? endpoint = null
)
{
    private readonly IHttpTransport transport =
        transport ?? throw new ArgumentNullException(nameof(transport));
    private readonly IReadOnlyList<IClientInterceptor> interceptors = [.. interceptors ?? []];
    private readonly ISmithyRetryStrategy? retryStrategy = retryStrategy;
    private readonly Uri? endpoint =
        endpoint is null || endpoint.IsAbsoluteUri
            ? endpoint
            : throw new ArgumentException("Endpoint must be an absolute URI.", nameof(endpoint));

    public async Task<TOutput> InvokeAsync<TInput, TOutput>(
        SmithyOperationBinding<TInput, TOutput> binding,
        TInput input,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(binding);

        var context = CreateContext(binding.ServiceName, binding.OperationName);
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

            var request = binding.Protocol.SerializeRequest(input);
            binding.ModifyRequest?.Invoke(request);
            var response = await SendAsync(
                    context,
                    request,
                    binding.ErrorDeserializer,
                    binding.Protocol.IsErrorResponse,
                    cancellationToken
                )
                .ConfigureAwait(false);

            var output = binding.Protocol.DeserializeResponse(response);
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

    public Task<TOutput> InvokeAsync<TInput, TOutput>(
        string serviceName,
        string operationName,
        IOperationProtocol<TInput, TOutput> protocol,
        TInput input,
        Action<SmithyHttpRequest>? modifyRequest = null,
        SmithyErrorDeserializer? errorDeserializer = null,
        CancellationToken cancellationToken = default
    )
    {
        return InvokeAsync(
            new SmithyOperationBinding<TInput, TOutput>(
                serviceName,
                operationName,
                protocol,
                modifyRequest,
                errorDeserializer
            ),
            input,
            cancellationToken
        );
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
        SmithyHttpRequest request,
        SmithyErrorDeserializer? errorDeserializer,
        Func<SmithyHttpResponse, bool>? isErrorResponse,
        CancellationToken cancellationToken
    )
    {
        for (var attempt = 1; ; attempt++)
        {
            context.Set(SmithyContextKeys.Attempt, attempt);
            var resolvedRequest = ApplyEndpoint(CloneRequest(request));
            var attemptRequest = await ApplyRequestInterceptorsAsync(
                    context,
                    resolvedRequest,
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

    private SmithyHttpRequest ApplyEndpoint(SmithyHttpRequest request)
    {
        if (endpoint is null || IsHttpAbsoluteUri(request.RequestUri))
        {
            return request;
        }

        return CloneRequest(request, ResolveRequestUri(endpoint, request.RequestUri));
    }

    private static SmithyHttpRequest CloneRequest(SmithyHttpRequest request) =>
        CloneRequest(request, request.RequestUri);

    private static SmithyHttpRequest CloneRequest(SmithyHttpRequest request, string requestUri)
    {
        var clone = new SmithyHttpRequest(request.Method, requestUri)
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

    private static string ResolveRequestUri(Uri endpoint, string requestUri)
    {
        var endpointText = endpoint.ToString().TrimEnd('/');
        var requestText = requestUri.TrimStart('/');
        return $"{endpointText}/{requestText}";
    }

    private static bool IsHttpAbsoluteUri(string requestUri)
    {
        return Uri.TryCreate(requestUri, UriKind.Absolute, out var uri)
            && uri.IsAbsoluteUri
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private SmithyContext CreateContext(string serviceName, string operationName)
    {
        var context = new SmithyContext();
        context.Set(SmithyContextKeys.ServiceName, serviceName);
        context.Set(SmithyContextKeys.OperationName, operationName);
        if (endpoint is not null)
        {
            context.Set(SmithyContextKeys.Endpoint, endpoint);
        }

        return context;
    }
}
