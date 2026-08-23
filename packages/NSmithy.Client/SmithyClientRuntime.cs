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
    IReadOnlyDictionary<string, ISmithyAuthScheme>? authSchemes = null,
    bool disableHostPrefixInjection = false,
    string? userAgent = null
)
{
    private static readonly IReadOnlyDictionary<string, ISmithyAuthScheme> NoAuthSchemes =
        new Dictionary<string, ISmithyAuthScheme>(StringComparer.Ordinal);
    private static readonly string DefaultUserAgent =
        $"NSmithy.Client/{typeof(SmithyClientRuntime).Assembly.GetName().Version?.ToString(3) ?? "unknown"}";

    private readonly IHttpTransport transport =
        transport ?? throw new ArgumentNullException(nameof(transport));

    // An array, not IReadOnlyList: this is foreach'd four times per invocation, and foreach over the
    // interface binds to IEnumerable<T>.GetEnumerator, which boxes an enumerator each time. Same
    // reasoning as the codec member-writer arrays.
    private readonly IClientInterceptor[] interceptors = [.. interceptors ?? []];
    private readonly ISmithyRetryStrategy? retryStrategy = retryStrategy;
    private readonly Uri? endpoint =
        endpoint is null || endpoint.IsAbsoluteUri
            ? endpoint
            : throw new ArgumentException("Endpoint must be an absolute URI.", nameof(endpoint));
    private readonly IEndpointResolver? endpointResolver =
        endpointResolver
        ?? (endpoint is { IsAbsoluteUri: true } ? new StaticEndpointResolver(endpoint) : null);
    private readonly IReadOnlyDictionary<string, ISmithyAuthScheme> authSchemes =
        authSchemes ?? NoAuthSchemes;
    private readonly bool disableHostPrefixInjection = disableHostPrefixInjection;
    private readonly string userAgent = userAgent is null
        ? DefaultUserAgent
        : !string.IsNullOrWhiteSpace(userAgent)
            ? userAgent
            : throw new ArgumentException("User-Agent must not be empty.", nameof(userAgent));
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
        if (CanInvokeDirect(binding))
        {
            return InvokeDirectAsync(binding, input, cancellationToken);
        }

        return InvokeCoreAsync(binding, input, cancellationToken);
    }

    private bool CanInvokeDirect<TInput, TOutput>(
        SmithyOperationBinding<TInput, TOutput> binding
    ) =>
        operationTimeout is null
        && retryStrategy is null
        && interceptors.Length == 0
        && (endpointResolver is null || endpointResolver.StaticEndpoint is not null)
        && (binding.AuthSchemeIds.Count == 0 || authSchemes.Count == 0)
        && !SmithyClientTelemetry.ActivitySource.HasListeners()
        && !SmithyClientTelemetry.Attempts.Enabled
        && !SmithyClientTelemetry.Errors.Enabled
        && !SmithyClientTelemetry.OperationDuration.Enabled;

    private async Task<TOutput> InvokeDirectAsync<TInput, TOutput>(
        SmithyOperationBinding<TInput, TOutput> binding,
        TInput input,
        CancellationToken cancellationToken
    )
    {
        var protocol = binding.Protocol;
        var resolvedEndpoint = ApplyHostPrefix(
            binding,
            input,
            endpointResolver?.StaticEndpoint
        );
        var request = PrepareAttemptRequest(
            protocol.SerializeRequest(input, cancellationToken),
            resolvedEndpoint,
            clone: false
        );
        var response = await transport
            .SendAsync(
                request,
                request.ExpectStreamingResponse
                    ? SmithyHttpClientResponseMode.Stream
                    : SmithyHttpClientResponseMode.Buffer,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (protocol.IsErrorResponse(response))
        {
            var error =
                await protocol
                    .DeserializeErrorAsync(response, cancellationToken)
                    .ConfigureAwait(false)
                ?? new SmithyClientException(response.StatusCode, response.ReasonPhrase);
            await DisposeBodyAsync(response).ConfigureAwait(false);
            throw error;
        }

        try
        {
            return await protocol
                .DeserializeResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await DisposeBodyAsync(response).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<TOutput> InvokeCoreAsync<TInput, TOutput>(
        SmithyOperationBinding<TInput, TOutput> binding,
        TInput input,
        CancellationToken callerToken
    )
    {
        // The operation timeout is a deadline over establishing the exchange — endpoint resolution,
        // serialization, every retry attempt, backoff delays, and receiving the response. Established
        // streaming response bodies are consumed lazily by the caller after this method returns, so
        // they use the caller token rather than this timeout source.
        using var timeoutSource = operationTimeout is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeoutSource?.CancelAfter(operationTimeout!.Value);
        var cancellationToken = timeoutSource?.Token ?? callerToken;

        var protocol = binding.Protocol;
        var needsContext =
            interceptors.Length > 0
            || retryStrategy is not null
            || (binding.AuthSchemeIds.Count > 0 && authSchemes.Count > 0);
        var context = needsContext ? CreateContext(binding) : null;

        // Both strings are constant per binding and materialized with it. Building them here meant a
        // string interpolation and a ShapeId.ToString() on every call, including the overwhelmingly
        // common case where no listener is subscribed and both results are thrown away.
        var tags = new TagList
        {
            { "rpc.system", "smithy" },
            { "rpc.service", binding.ServiceIdTag },
            { "rpc.method", binding.OperationId.Name },
        };
        using var activity = SmithyClientTelemetry.ActivitySource.StartActivity(
            binding.ActivityName,
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

        Exception? executionError = null;
        try
        {
            // Endpoint resolution runs once per invocation, before any lifecycle hooks, so
            // interceptors observe the effective endpoint from OnBeforeExecution on. It remains
            // inside the execution scope so deadlines and completion interceptors observe failures.
            //
            // A resolver that answers StaticEndpoint returns the same endpoint whatever it is handed,
            // so the parameters are skipped rather than allocated and discarded. That is the common
            // case — most clients are configured with one fixed endpoint — and it also keeps the await,
            // and its state machine, off the path entirely.
            var resolvedEndpoint = endpointResolver?.StaticEndpoint;
            if (endpointResolver is not null && resolvedEndpoint is null)
            {
                resolvedEndpoint = await endpointResolver
                    .ResolveEndpointAsync(
                        new SmithyEndpointParameters(
                            binding.ServiceId,
                            binding.OperationId,
                            endpoint,
                            input
                        ),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            resolvedEndpoint = ApplyHostPrefix(binding, input, resolvedEndpoint);

            var authScheme = SmithyAuthSchemeResolver.SelectScheme(
                binding.AuthSchemeIds,
                resolvedEndpoint?.AuthSchemes,
                authSchemes
            );
            if (context is not null && resolvedEndpoint is not null)
            {
                context.Set(SmithyContextKeys.Endpoint, resolvedEndpoint.Uri);
                context.Set(SmithyContextKeys.ResolvedEndpoint, resolvedEndpoint);
            }
            if (context is not null && authScheme is not null)
            {
                context.Set(SmithyContextKeys.AuthSchemeId, authScheme.SchemeId);
            }
            var identityProperties = authScheme is null
                ? null
                : new SmithyIdentityProperties(
                    binding.ServiceId,
                    binding.OperationId,
                    resolvedEndpoint
                );

            foreach (var interceptor in interceptors)
            {
                interceptor.OnBeforeExecution(context!);
            }

            foreach (var interceptor in interceptors)
            {
                interceptor.OnBeforeSerialization(context!, input);
            }

            var request = protocol.SerializeRequest(input, callerToken);
            var response = await SendUnaryAsync(
                    context,
                    request,
                    protocol,
                    tags,
                    resolvedEndpoint,
                    authScheme,
                    identityProperties,
                    cancellationToken
                )
                .ConfigureAwait(false);

            var deserializationToken =
                response.Body is SmithyHttpBody.Streaming ? callerToken : cancellationToken;

            TOutput output;
            try
            {
                output = await protocol
                    .DeserializeResponseAsync(response, deserializationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                // The protocol failed before the output took ownership of a streaming body.
                await DisposeBodyAsync(response).ConfigureAwait(false);
                throw;
            }
            for (var i = interceptors.Length - 1; i >= 0; i--)
            {
                interceptors[i].OnAfterDeserialization(context!, output);
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
            for (var i = interceptors.Length - 1; i >= 0; i--)
            {
                interceptors[i].OnAfterExecution(context!, executionError);
            }

            if (executionError is not null)
            {
                activity?.SetStatus(ActivityStatusCode.Error, executionError.Message);
                activity?.SetTag("error.type", executionError.GetType().FullName);
                if (SmithyClientTelemetry.Errors.Enabled)
                {
                    var errorTags = tags;
                    errorTags.Add("error.type", executionError.GetType().FullName);
                    SmithyClientTelemetry.Errors.Add(1, errorTags);
                }
            }

            // Guarded rather than always recorded: with no meter subscribed the instrument discards
            // the measurement anyway, and the guard keeps the elapsed-time computation and the tag
            // copy off the unsubscribed path.
            if (SmithyClientTelemetry.OperationDuration.Enabled)
            {
                SmithyClientTelemetry.OperationDuration.Record(
                    Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds,
                    tags
                );
            }
        }
    }

    private async Task<SmithyHttpClientResponse> SendUnaryAsync<TInput, TOutput>(
        SmithyContext? context,
        SmithyHttpRequest request,
        IClientOperationProtocol<TInput, TOutput> protocol,
        TagList tags,
        SmithyEndpoint? resolvedEndpoint,
        ISmithyAuthScheme? authScheme,
        SmithyIdentityProperties? identityProperties,
        CancellationToken cancellationToken
    )
    {
        var session = retryStrategy?.Begin();
        // Streaming request bodies cannot be replayed, so they get exactly one attempt.
        var canRetry = session is not null && IsReplayable(request.Body);
        for (var attempt = 1; ; attempt++)
        {
            context?.Set(SmithyContextKeys.Attempt, attempt);
            using var attemptActivity = SmithyClientTelemetry.ActivitySource.StartActivity(
                "attempt",
                ActivityKind.Internal
            );
            attemptActivity?.SetTag("smithy.attempt", attempt);
            if (SmithyClientTelemetry.Attempts.Enabled)
            {
                SmithyClientTelemetry.Attempts.Add(1, tags);
            }

            var attemptRequest = PrepareAttemptRequest(request, resolvedEndpoint, canRetry);
            if (interceptors.Length > 0 || authScheme is not null)
            {
                attemptRequest = await ApplyRequestInterceptorsAsync(
                        context!,
                        attemptRequest,
                        authScheme,
                        identityProperties,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            SmithyHttpClientResponse response;
            try
            {
                response = await transport
                    .SendAsync(
                        attemptRequest,
                        attemptRequest.ExpectStreamingResponse
                            ? SmithyHttpClientResponseMode.Stream
                            : SmithyHttpClientResponseMode.Buffer,
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
                    new SmithyRetryOutcome(attempt, null, transportError, context!)
                );
                if (!decision.ShouldRetry)
                {
                    throw;
                }

                await DelayAsync(decision.Delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            for (var i = interceptors.Length - 1; i >= 0; i--)
            {
                interceptors[i].OnAfterTransmit(context!, response);
            }

            if (!protocol.IsErrorResponse(response))
            {
                session?.RecordSuccess();
                return response;
            }

            var error =
                await protocol
                    .DeserializeErrorAsync(response, cancellationToken)
                    .ConfigureAwait(false)
                ?? new SmithyClientException(response.StatusCode, response.ReasonPhrase);

            attemptActivity?.SetStatus(ActivityStatusCode.Error, error.Message);
            attemptActivity?.SetTag("error.type", error.GetType().FullName);

            // The error path abandons the response, so a streaming body (which holds the live
            // HTTP connection) must be released here — whether we retry or throw.
            await DisposeBodyAsync(response).ConfigureAwait(false);

            if (canRetry)
            {
                var decision = session!.Classify(
                    new SmithyRetryOutcome(attempt, response, error, context!)
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

    private static ValueTask DisposeBodyAsync(SmithyHttpClientResponse response) =>
        response.Body is SmithyHttpBody.Streaming streaming
            ? streaming.Content.DisposeAsync()
            : ValueTask.CompletedTask;

    private static Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        delay > TimeSpan.Zero ? Task.Delay(delay, cancellationToken) : Task.CompletedTask;

    private static bool IsReplayable(SmithyHttpBody body) =>
        body is not SmithyHttpBody.Streaming and not SmithyHttpBody.EventStreaming;

    private async Task<SmithyHttpRequest> ApplyRequestInterceptorsAsync(
        SmithyContext context,
        SmithyHttpRequest request,
        ISmithyAuthScheme? authScheme,
        SmithyIdentityProperties? identityProperties,
        CancellationToken cancellationToken
    )
    {
        // User interceptors finish request preparation before the selected signer sees it.
        foreach (var interceptor in interceptors)
        {
            request =
                await interceptor
                    .OnBeforeSigningAsync(context, request, cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"{interceptor.GetType().Name}.{nameof(IClientInterceptor.OnBeforeSigningAsync)} returned null."
                );
        }

        if (authScheme is not null)
        {
            var identity =
                await authScheme
                    .IdentityResolver.ResolveIdentityAsync(identityProperties!, cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"{authScheme.IdentityResolver.GetType().Name} returned a null identity."
                );
            request =
                await authScheme
                    .Signer.SignAsync(context, request, identity, cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"{authScheme.Signer.GetType().Name}.{nameof(ISmithySigner.SignAsync)} returned null."
                );
        }

        foreach (var interceptor in interceptors)
        {
            request =
                await interceptor
                    .OnBeforeTransmitAsync(context, request, cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"{interceptor.GetType().Name}.{nameof(IClientInterceptor.OnBeforeTransmitAsync)} returned null."
                );
        }

        return request;
    }

    private SmithyHttpRequest PrepareAttemptRequest(
        SmithyHttpRequest request,
        SmithyEndpoint? resolvedEndpoint,
        bool clone
    )
    {
        var resolved = clone ? CloneRequest(request, request.RequestUri) : request;
        if (resolvedEndpoint is not null && !IsHttpAbsoluteUri(resolved.RequestUri))
        {
            resolved.RequestUri = ResolveRequestUri(resolvedEndpoint.Uri, resolved.RequestUri);
        }

        if (resolvedEndpoint?.Headers is not null)
        {
            foreach (var header in resolvedEndpoint.Headers)
            {
                resolved.Headers[header.Key] = [header.Value];
            }
        }

        if (!resolved.Headers.ContainsKey("User-Agent"))
        {
            resolved.Headers["User-Agent"] = [userAgent];
        }

        return resolved;
    }

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

    private SmithyEndpoint? ApplyHostPrefix<TInput, TOutput>(
        SmithyOperationBinding<TInput, TOutput> binding,
        TInput input,
        SmithyEndpoint? resolvedEndpoint
    )
    {
        if (disableHostPrefixInjection || binding.HostPrefix is null)
        {
            return resolvedEndpoint;
        }

        if (resolvedEndpoint is null)
        {
            throw new InvalidOperationException(
                $"Operation '{binding.OperationId}' models an endpoint host prefix, but no endpoint was resolved."
            );
        }

        return SmithyHostPrefix.Apply(resolvedEndpoint, binding.HostPrefix(input));
    }

    // Service/operation ids and names, resolved endpoint/URI, auth scheme, and attempt.
    private const int ContextKeyCount = 8;

    private SmithyContext CreateContext<TInput, TOutput>(
        SmithyOperationBinding<TInput, TOutput> binding
    )
    {
        var context = new SmithyContext(ContextKeyCount);
        context.Set(SmithyContextKeys.ServiceId, binding.ServiceId);
        context.Set(SmithyContextKeys.ServiceName, binding.ServiceId.Name);
        context.Set(SmithyContextKeys.OperationId, binding.OperationId);
        context.Set(SmithyContextKeys.OperationName, binding.OperationId.Name);
        if (endpoint is not null)
        {
            context.Set(SmithyContextKeys.Endpoint, endpoint);
        }

        return context;
    }
}
