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

    public Task<TOutput> InvokeAsync<TInput, TOutput>(
        SmithyOperationBinding<TInput, TOutput> binding,
        TInput input,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(binding);
        return InvokeCoreAsync(binding, input, cancellationToken);
    }

    private async Task<TOutput> InvokeCoreAsync<TInput, TOutput>(
        SmithyOperationBinding<TInput, TOutput> binding,
        TInput input,
        CancellationToken cancellationToken
    )
    {
        var protocol = binding.Protocol;
        var context = CreateContext(binding.ServiceId.Name, binding.OperationId.Name);
        foreach (var interceptor in interceptors)
        {
            interceptor.OnBeforeExecution(context);
        }

        Exception? executionError = null;
        try
        {
            foreach (var interceptor in interceptors)
            {
                interceptor.OnBeforeSerialization(context, input);
            }

            var request = protocol.SerializeRequest(input);
            var response = await SendAsync(
                    context,
                    request,
                    protocol.DeserializeErrorAsync,
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
        catch (Exception error)
        {
            executionError = error;
            throw;
        }
        finally
        {
            for (var i = interceptors.Count - 1; i >= 0; i--)
            {
                interceptors[i].OnAfterExecution(context, executionError);
            }
        }
    }

    private async Task<SmithyHttpResponse> SendAsync(
        SmithyContext context,
        SmithyHttpRequest request,
        Func<SmithyHttpResponse, CancellationToken, ValueTask<Exception?>> deserializeError,
        Func<SmithyHttpResponse, bool> isErrorResponse,
        CancellationToken cancellationToken
    )
    {
        var session = retryStrategy?.Begin();
        // Streaming request bodies cannot be replayed, so they get exactly one attempt.
        var canRetry = session is not null && request.Body is not SmithyHttpBody.Streaming;
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

            SmithyHttpResponse response;
            try
            {
                response = await transport
                    .SendAsync(attemptRequest, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception transportError)
                when (canRetry && !cancellationToken.IsCancellationRequested)
            {
                var decision = session!.Classify(
                    new SmithyRetryOutcome(attempt, null, transportError, context)
                );
                if (!decision.ShouldRetry)
                {
                    throw;
                }

                await DelayAsync(decision.Delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            for (var i = interceptors.Count - 1; i >= 0; i--)
            {
                interceptors[i].OnAfterTransmit(context, response);
            }

            if (!isErrorResponse(response))
            {
                session?.RecordSuccess();
                return response;
            }

            var error =
                await deserializeError(response, cancellationToken).ConfigureAwait(false)
                ?? new SmithyClientException(response.StatusCode, response.ReasonPhrase);

            if (canRetry)
            {
                var decision = session!.Classify(
                    new SmithyRetryOutcome(attempt, response, error, context)
                );
                if (decision.ShouldRetry)
                {
                    await DelayAsync(decision.Delay, cancellationToken).ConfigureAwait(false);
                    continue;
                }
            }

            throw error;
        }
    }

    private static Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        delay > TimeSpan.Zero ? Task.Delay(delay, cancellationToken) : Task.CompletedTask;

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
            Body = request.Body,
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
