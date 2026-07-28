using System.Diagnostics;
using System.Runtime.CompilerServices;
using NSmithy.Http;

namespace NSmithy.Client;

public sealed class SmithyClientRuntime(
    IHttpTransport transport,
    IEnumerable<IClientInterceptor>? interceptors = null,
    ISmithyRetryStrategy? retryStrategy = null,
    Uri? endpoint = null,
    TimeSpan? operationTimeout = null,
    IEndpointResolver? endpointResolver = null,
    IReadOnlyDictionary<string, IClientInterceptor>? authSchemes = null
)
{
    private static readonly IReadOnlyDictionary<string, IClientInterceptor> NoAuthSchemes =
        new Dictionary<string, IClientInterceptor>(StringComparer.Ordinal);

    private readonly IHttpTransport transport =
        transport ?? throw new ArgumentNullException(nameof(transport));
    private readonly IReadOnlyList<IClientInterceptor> interceptors = [.. interceptors ?? []];
    private readonly ISmithyRetryStrategy? retryStrategy = retryStrategy;
    private readonly Uri? endpoint =
        endpoint is null || endpoint.IsAbsoluteUri
            ? endpoint
            : throw new ArgumentException("Endpoint must be an absolute URI.", nameof(endpoint));
    private readonly IEndpointResolver? endpointResolver =
        endpointResolver
        ?? (endpoint is { IsAbsoluteUri: true } ? new StaticEndpointResolver(endpoint) : null);
    private readonly IReadOnlyDictionary<string, IClientInterceptor> authSchemes =
        authSchemes ?? NoAuthSchemes;
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

    public IAsyncEnumerable<TOutputEvent> InvokeOutputStreamAsync<TInput, TOutputEvent>(
        SmithyOutputEventStreamOperationBinding<TInput, TOutputEvent> binding,
        TInput input,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(binding);
        return InvokeOutputStreamCoreAsync(binding, input, cancellationToken);
    }

    public Task<TOutput> InvokeInputStreamAsync<TInputEvent, TOutput>(
        SmithyInputEventStreamOperationBinding<TInputEvent, TOutput> binding,
        IAsyncEnumerable<TInputEvent> input,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(input);
        return InvokeInputStreamCoreAsync(binding, input, cancellationToken);
    }

    public IAsyncEnumerable<TOutputEvent> InvokeDuplexStreamAsync<TInputEvent, TOutputEvent>(
        SmithyDuplexEventStreamOperationBinding<TInputEvent, TOutputEvent> binding,
        IAsyncEnumerable<TInputEvent> input,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(input);
        return InvokeDuplexStreamCoreAsync(binding, input, cancellationToken);
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

        // Endpoint resolution runs once per invocation, before any lifecycle hooks, so
        // interceptors observe the effective endpoint from OnBeforeExecution on. A static
        // endpoint resolves via StaticEndpointResolver at effectively zero cost.
        SmithyEndpoint? resolvedEndpoint = null;
        if (endpointResolver is not null)
        {
            resolvedEndpoint = await endpointResolver
                .ResolveEndpointAsync(
                    new SmithyEndpointParameters(
                        binding.ServiceId,
                        binding.OperationId,
                        endpoint,
                        input
                    ),
                    callerToken
                )
                .ConfigureAwait(false);
            context.Set(SmithyContextKeys.Endpoint, resolvedEndpoint.Uri);
        }

        var authInterceptor = SmithyAuthSchemeResolver.SelectInterceptor(
            binding.AuthSchemeIds,
            resolvedEndpoint?.AuthSchemes,
            authSchemes
        );

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
            var response = await SendUnaryAsync(
                    context,
                    request,
                    protocol.DeserializeErrorAsync,
                    protocol.IsErrorResponse,
                    tags,
                    resolvedEndpoint,
                    authInterceptor,
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

    private async Task<SmithyHttpResponse> SendUnaryAsync(
        SmithyContext context,
        SmithyHttpRequest request,
        Func<SmithyHttpResponse, CancellationToken, ValueTask<Exception?>> deserializeError,
        Func<SmithyHttpResponse, bool> isErrorResponse,
        TagList tags,
        SmithyEndpoint? resolvedEndpoint,
        IClientInterceptor? authInterceptor,
        CancellationToken cancellationToken
    )
    {
        var session = retryStrategy?.Begin();
        // Streaming request bodies cannot be replayed, so they get exactly one attempt.
        var canRetry = session is not null && IsReplayable(request.Body);
        for (var attempt = 1; ; attempt++)
        {
            context.Set(SmithyContextKeys.Attempt, attempt);
            using var attemptActivity = SmithyClientTelemetry.ActivitySource.StartActivity(
                "attempt",
                ActivityKind.Internal
            );
            attemptActivity?.SetTag("smithy.attempt", attempt);
            SmithyClientTelemetry.Attempts.Add(1, tags);

            var resolvedRequest = ApplyEndpoint(CloneRequest(request), resolvedEndpoint);
            var attemptRequest = await ApplyRequestInterceptorsAsync(
                    context,
                    resolvedRequest,
                    authInterceptor,
                    cancellationToken
                )
                .ConfigureAwait(false);

            SmithyHttpResponse response;
            try
            {
                response = await transport
                    .SendAsync(
                        attemptRequest,
                        attemptRequest.ExpectStreamingResponse
                            ? SmithyHttpResponseMode.Stream
                            : SmithyHttpResponseMode.Buffer,
                        cancellationToken
                    )
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

    private async Task<SmithyHttpResponse> SendStreamingAsync(
        SmithyContext context,
        SmithyHttpRequest request,
        TagList tags,
        SmithyEndpoint? resolvedEndpoint,
        IClientInterceptor? authInterceptor,
        CancellationToken cancellationToken
    )
    {
        context.Set(SmithyContextKeys.Attempt, 1);
        using var attemptActivity = SmithyClientTelemetry.ActivitySource.StartActivity(
            "attempt",
            ActivityKind.Internal
        );
        attemptActivity?.SetTag("smithy.attempt", 1);
        SmithyClientTelemetry.Attempts.Add(1, tags);

        var resolvedRequest = ApplyEndpoint(CloneRequest(request), resolvedEndpoint);
        var attemptRequest = await ApplyRequestInterceptorsAsync(
                context,
                resolvedRequest,
                authInterceptor,
                cancellationToken
            )
            .ConfigureAwait(false);

        var response = await transport
            .SendAsync(attemptRequest, SmithyHttpResponseMode.Stream, cancellationToken)
            .ConfigureAwait(false);

        for (var i = interceptors.Count - 1; i >= 0; i--)
        {
            interceptors[i].OnAfterTransmit(context, response);
        }

        return response;
    }

    private async IAsyncEnumerable<TOutputEvent> InvokeOutputStreamCoreAsync<TInput, TOutputEvent>(
        SmithyOutputEventStreamOperationBinding<TInput, TOutputEvent> binding,
        TInput input,
        [EnumeratorCancellation] CancellationToken callerToken
    )
    {
        using var timeoutSource = operationTimeout is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeoutSource?.CancelAfter(operationTimeout!.Value);
        var cancellationToken = timeoutSource?.Token ?? callerToken;

        var protocol = binding.Protocol;
        var context = CreateContext(binding.ServiceId.Name, binding.OperationId.Name);
        var (resolvedEndpoint, authInterceptor) = await ResolveEndpointAndAuthAsync(
                binding.ServiceId,
                binding.OperationId,
                binding.AuthSchemeIds,
                input,
                context,
                callerToken
            )
            .ConfigureAwait(false);
        var tags = CreateTags(binding.ServiceId.ToString(), binding.OperationId.Name);
        using var activity = StartOperationActivity(
            binding.ServiceId.Name,
            binding.OperationId.Name,
            tags
        );
        var startTimestamp = Stopwatch.GetTimestamp();
        Exception? executionError = null;

        foreach (var interceptor in interceptors)
        {
            interceptor.OnBeforeExecution(context);
        }

        IAsyncEnumerator<TOutputEvent>? events = null;
        try
        {
            foreach (var interceptor in interceptors)
            {
                interceptor.OnBeforeSerialization(context, input);
            }

            var request = protocol.SerializeRequest(input);
            var response = await SendStreamingAsync(
                    context,
                    request,
                    tags,
                    resolvedEndpoint,
                    authInterceptor,
                    cancellationToken
                )
                .ConfigureAwait(false);
            events = protocol
                .DeserializeResponseEventsAsync(response, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
        }
        catch (OperationCanceledException)
            when (timeoutSource?.IsCancellationRequested == true
                && !callerToken.IsCancellationRequested
            )
        {
            var timeoutError = new TimeoutException(
                $"The operation did not complete within {operationTimeout!.Value}."
            );
            executionError = timeoutError;
            CompleteExecution(context, executionError, activity, startTimestamp, tags);
            throw timeoutError;
        }
        catch (Exception error)
        {
            executionError = error;
            CompleteExecution(context, executionError, activity, startTimestamp, tags);
            throw;
        }

        try
        {
            while (true)
            {
                TOutputEvent item;
                try
                {
                    if (!await events.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }

                    item = events.Current;
                }
                catch (OperationCanceledException)
                    when (timeoutSource?.IsCancellationRequested == true
                        && !callerToken.IsCancellationRequested
                    )
                {
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

                for (var i = interceptors.Count - 1; i >= 0; i--)
                {
                    interceptors[i].OnAfterDeserialization(context, item);
                }

                yield return item;
            }
        }
        finally
        {
            if (events is not null)
            {
                await events.DisposeAsync().ConfigureAwait(false);
            }

            CompleteExecution(context, executionError, activity, startTimestamp, tags);
        }
    }

    private async Task<TOutput> InvokeInputStreamCoreAsync<TInputEvent, TOutput>(
        SmithyInputEventStreamOperationBinding<TInputEvent, TOutput> binding,
        IAsyncEnumerable<TInputEvent> input,
        CancellationToken callerToken
    )
    {
        using var timeoutSource = operationTimeout is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeoutSource?.CancelAfter(operationTimeout!.Value);
        var cancellationToken = timeoutSource?.Token ?? callerToken;

        var protocol = binding.Protocol;
        var context = CreateContext(binding.ServiceId.Name, binding.OperationId.Name);
        var (resolvedEndpoint, authInterceptor) = await ResolveEndpointAndAuthAsync(
                binding.ServiceId,
                binding.OperationId,
                binding.AuthSchemeIds,
                input,
                context,
                callerToken
            )
            .ConfigureAwait(false);
        var tags = CreateTags(binding.ServiceId.ToString(), binding.OperationId.Name);
        using var activity = StartOperationActivity(
            binding.ServiceId.Name,
            binding.OperationId.Name,
            tags
        );
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

            var request = protocol.SerializeRequest(input, cancellationToken);
            var response = await SendStreamingAsync(
                    context,
                    request,
                    tags,
                    resolvedEndpoint,
                    authInterceptor,
                    cancellationToken
                )
                .ConfigureAwait(false);
            var output = await protocol
                .DeserializeResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);

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
            CompleteExecution(context, executionError, activity, startTimestamp, tags);
        }
    }

    private async IAsyncEnumerable<TOutputEvent> InvokeDuplexStreamCoreAsync<
        TInputEvent,
        TOutputEvent
    >(
        SmithyDuplexEventStreamOperationBinding<TInputEvent, TOutputEvent> binding,
        IAsyncEnumerable<TInputEvent> input,
        [EnumeratorCancellation] CancellationToken callerToken
    )
    {
        using var timeoutSource = operationTimeout is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeoutSource?.CancelAfter(operationTimeout!.Value);
        var cancellationToken = timeoutSource?.Token ?? callerToken;

        var protocol = binding.Protocol;
        var context = CreateContext(binding.ServiceId.Name, binding.OperationId.Name);
        var (resolvedEndpoint, authInterceptor) = await ResolveEndpointAndAuthAsync(
                binding.ServiceId,
                binding.OperationId,
                binding.AuthSchemeIds,
                input,
                context,
                callerToken
            )
            .ConfigureAwait(false);
        var tags = CreateTags(binding.ServiceId.ToString(), binding.OperationId.Name);
        using var activity = StartOperationActivity(
            binding.ServiceId.Name,
            binding.OperationId.Name,
            tags
        );
        var startTimestamp = Stopwatch.GetTimestamp();
        Exception? executionError = null;

        foreach (var interceptor in interceptors)
        {
            interceptor.OnBeforeExecution(context);
        }

        IAsyncEnumerator<TOutputEvent>? events = null;
        try
        {
            foreach (var interceptor in interceptors)
            {
                interceptor.OnBeforeSerialization(context, input);
            }

            var request = protocol.SerializeRequest(input, cancellationToken);
            var response = await SendStreamingAsync(
                    context,
                    request,
                    tags,
                    resolvedEndpoint,
                    authInterceptor,
                    cancellationToken
                )
                .ConfigureAwait(false);
            events = protocol
                .DeserializeResponseEventsAsync(response, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
        }
        catch (OperationCanceledException)
            when (timeoutSource?.IsCancellationRequested == true
                && !callerToken.IsCancellationRequested
            )
        {
            var timeoutError = new TimeoutException(
                $"The operation did not complete within {operationTimeout!.Value}."
            );
            executionError = timeoutError;
            CompleteExecution(context, executionError, activity, startTimestamp, tags);
            throw timeoutError;
        }
        catch (Exception error)
        {
            executionError = error;
            CompleteExecution(context, executionError, activity, startTimestamp, tags);
            throw;
        }

        try
        {
            while (true)
            {
                TOutputEvent item;
                try
                {
                    if (!await events.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }

                    item = events.Current;
                }
                catch (OperationCanceledException)
                    when (timeoutSource?.IsCancellationRequested == true
                        && !callerToken.IsCancellationRequested
                    )
                {
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

                for (var i = interceptors.Count - 1; i >= 0; i--)
                {
                    interceptors[i].OnAfterDeserialization(context, item);
                }

                yield return item;
            }
        }
        finally
        {
            if (events is not null)
            {
                await events.DisposeAsync().ConfigureAwait(false);
            }

            CompleteExecution(context, executionError, activity, startTimestamp, tags);
        }
    }

    private static Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        delay > TimeSpan.Zero ? Task.Delay(delay, cancellationToken) : Task.CompletedTask;

    private async Task<(
        SmithyEndpoint? Endpoint,
        IClientInterceptor? AuthInterceptor
    )> ResolveEndpointAndAuthAsync(
        NSmithy.Core.ShapeId serviceId,
        NSmithy.Core.ShapeId operationId,
        IReadOnlyList<string> authSchemeIds,
        object? input,
        SmithyContext context,
        CancellationToken cancellationToken
    )
    {
        SmithyEndpoint? resolvedEndpoint = null;
        if (endpointResolver is not null)
        {
            resolvedEndpoint = await endpointResolver
                .ResolveEndpointAsync(
                    new SmithyEndpointParameters(serviceId, operationId, endpoint, input),
                    cancellationToken
                )
                .ConfigureAwait(false);
            context.Set(SmithyContextKeys.Endpoint, resolvedEndpoint.Uri);
        }

        var authInterceptor = SmithyAuthSchemeResolver.SelectInterceptor(
            authSchemeIds,
            resolvedEndpoint?.AuthSchemes,
            authSchemes
        );
        return (resolvedEndpoint, authInterceptor);
    }

    private static TagList CreateTags(string serviceId, string operationName) =>
        new()
        {
            { "rpc.system", "smithy" },
            { "rpc.service", serviceId },
            { "rpc.method", operationName },
        };

    private static Activity? StartOperationActivity(
        string serviceName,
        string operationName,
        TagList tags
    )
    {
        var activity = SmithyClientTelemetry.ActivitySource.StartActivity(
            $"{serviceName}.{operationName}",
            ActivityKind.Client
        );
        if (activity is not null)
        {
            foreach (var tag in tags)
            {
                activity.SetTag(tag.Key, tag.Value);
            }
        }

        return activity;
    }

    private void CompleteExecution(
        SmithyContext context,
        Exception? executionError,
        Activity? activity,
        long startTimestamp,
        TagList tags
    )
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

    private static bool IsReplayable(SmithyHttpBody body) =>
        body is not SmithyHttpBody.Streaming and not SmithyHttpBody.EventStreaming;

    private async Task<SmithyHttpRequest> ApplyRequestInterceptorsAsync(
        SmithyContext context,
        SmithyHttpRequest request,
        IClientInterceptor? authInterceptor,
        CancellationToken cancellationToken
    )
    {
        // The selected auth interceptor runs after user interceptors in each request phase, so
        // signing sees the final request and nothing mutates it afterwards.
        foreach (var interceptor in interceptors)
        {
            request = await interceptor
                .OnBeforeSigningAsync(context, request, cancellationToken)
                .ConfigureAwait(false);
        }

        if (authInterceptor is not null)
        {
            request = await authInterceptor
                .OnBeforeSigningAsync(context, request, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var interceptor in interceptors)
        {
            request = await interceptor
                .OnBeforeTransmitAsync(context, request, cancellationToken)
                .ConfigureAwait(false);
        }

        if (authInterceptor is not null)
        {
            request = await authInterceptor
                .OnBeforeTransmitAsync(context, request, cancellationToken)
                .ConfigureAwait(false);
        }

        return request;
    }

    private static SmithyHttpRequest ApplyEndpoint(
        SmithyHttpRequest request,
        SmithyEndpoint? resolvedEndpoint
    )
    {
        if (resolvedEndpoint is null)
        {
            return request;
        }

        var resolved = IsHttpAbsoluteUri(request.RequestUri)
            ? request
            : CloneRequest(request, ResolveRequestUri(resolvedEndpoint.Uri, request.RequestUri));

        if (resolvedEndpoint.Headers is not null)
        {
            foreach (var header in resolvedEndpoint.Headers)
            {
                resolved.Headers[header.Key] = [header.Value];
            }
        }

        return resolved;
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
