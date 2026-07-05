using System.Diagnostics;
using NSmithy.Http;

namespace NSmithy.Client;

public sealed class SmithyClientRuntime(
    IHttpTransport transport,
    IEnumerable<IClientInterceptor>? interceptors = null,
    ISmithyRetryStrategy? retryStrategy = null,
    Uri? endpoint = null,
    TimeSpan? operationTimeout = null
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
    private readonly TimeSpan? operationTimeout =
        operationTimeout is null || operationTimeout > TimeSpan.Zero
            ? operationTimeout
            : throw new ArgumentOutOfRangeException(
                nameof(operationTimeout),
                operationTimeout,
                "Operation timeout must be positive."
            );

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
        CancellationToken callerToken
    )
    {
        // The operation timeout is a deadline over the whole execution — serialization, every
        // retry attempt, and backoff delays — enforced through a linked token so in-flight
        // transport work is cancelled promptly.
        using var timeoutSource = operationTimeout is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeoutSource?.CancelAfter(operationTimeout!.Value);
        var cancellationToken = timeoutSource?.Token ?? callerToken;

        var protocol = binding.Protocol;
        var context = CreateContext(binding.ServiceId.Name, binding.OperationId.Name);

        var tags = new TagList
        {
            { "rpc.system", "smithy" },
            { "rpc.service", binding.ServiceId.ToString() },
            { "rpc.method", binding.OperationId.Name },
        };
        using var activity = SmithyClientTelemetry.ActivitySource.StartActivity(
            $"{binding.ServiceId.Name}.{binding.OperationId.Name}",
            ActivityKind.Client
        );
        if (activity is not null)
        {
            foreach (var tag in tags)
            {
                activity.SetTag(tag.Key, tag.Value);
            }
        }

        var startTimestamp = Stopwatch.GetTimestamp();

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
                    tags,
                    cancellationToken
                )
                .ConfigureAwait(false);

            TOutput output;
            try
            {
                output = protocol.DeserializeResponse(response);
            }
            catch
            {
                // The protocol failed before the output took ownership of a streaming body.
                await DisposeBodyAsync(response).ConfigureAwait(false);
                throw;
            }
            for (var i = interceptors.Count - 1; i >= 0; i--)
            {
                interceptors[i].OnAfterDeserialization(context, output);
            }

            return output;
        }
        catch (OperationCanceledException)
            when (timeoutSource?.IsCancellationRequested == true
                && !callerToken.IsCancellationRequested
            )
        {
            // The deadline fired, not the caller: surface a TimeoutException so the two are
            // distinguishable. Interceptors observe the translated exception.
            var timeoutError = new TimeoutException(
                $"The operation did not complete within {operationTimeout!.Value}."
            );
            executionError = timeoutError;
            throw timeoutError;
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

            if (executionError is not null)
            {
                activity?.SetStatus(ActivityStatusCode.Error, executionError.Message);
                activity?.SetTag("error.type", executionError.GetType().FullName);
                var errorTags = tags;
                errorTags.Add("error.type", executionError.GetType().FullName);
                SmithyClientTelemetry.Errors.Add(1, errorTags);
            }

            SmithyClientTelemetry.OperationDuration.Record(
                Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds,
                tags
            );
        }
    }

    private async Task<SmithyHttpResponse> SendAsync(
        SmithyContext context,
        SmithyHttpRequest request,
        Func<SmithyHttpResponse, CancellationToken, ValueTask<Exception?>> deserializeError,
        Func<SmithyHttpResponse, bool> isErrorResponse,
        TagList tags,
        CancellationToken cancellationToken
    )
    {
        var session = retryStrategy?.Begin();
        // Streaming request bodies cannot be replayed, so they get exactly one attempt.
        var canRetry = session is not null && request.Body is not SmithyHttpBody.Streaming;
        for (var attempt = 1; ; attempt++)
        {
            context.Set(SmithyContextKeys.Attempt, attempt);
            using var attemptActivity = SmithyClientTelemetry.ActivitySource.StartActivity(
                "attempt",
                ActivityKind.Internal
            );
            attemptActivity?.SetTag("smithy.attempt", attempt);
            SmithyClientTelemetry.Attempts.Add(1, tags);

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
                attemptActivity?.SetStatus(ActivityStatusCode.Error, transportError.Message);
                attemptActivity?.SetTag("error.type", transportError.GetType().FullName);
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

            attemptActivity?.SetStatus(ActivityStatusCode.Error, error.Message);
            attemptActivity?.SetTag("error.type", error.GetType().FullName);

            // The error path abandons the response, so a streaming body (which holds the live
            // HTTP connection) must be released here — whether we retry or throw.
            await DisposeBodyAsync(response).ConfigureAwait(false);

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

    private static ValueTask DisposeBodyAsync(SmithyHttpResponse response) =>
        response.Body is SmithyHttpBody.Streaming streaming
            ? streaming.Content.DisposeAsync()
            : ValueTask.CompletedTask;

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
