using NSmithy.Http;

namespace NSmithy.Client;

/// <summary>
/// Common, protocol-agnostic client configuration. Generated clients take a service-specific
/// subclass (<c>{Service}ClientConfig</c>) so service-specific options can be added later without
/// changing the client constructor surface.
/// </summary>
public class SmithyClientConfig
{
    public SmithyClientConfig() { }

    /// <summary>
    /// Copies all common options from <paramref name="source"/>. The copy is shallow on
    /// purpose: retry strategies, interceptors, and auth schemes are shared by reference, so a
    /// strategy instance (and client-wide state such as its retry quota) can be deliberately
    /// shared across clients. Generated clients copy the caller's config at construction, so
    /// constructing a client never mutates the config the caller passed in.
    /// </summary>
    protected SmithyClientConfig(SmithyClientConfig source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Endpoint = source.Endpoint;
        Protocol = source.Protocol;
        RetryStrategy = source.RetryStrategy;
        OperationTimeout = source.OperationTimeout;
        IdempotencyTokenProvider = source.IdempotencyTokenProvider;
        foreach (var interceptor in source.Interceptors)
        {
            Interceptors.Add(interceptor);
        }

        foreach (var authScheme in source.AuthSchemes)
        {
            AuthSchemes.Add(authScheme);
        }
    }

    /// <summary>
    /// The service endpoint. Set by the endpoint constructor, and optional when an
    /// <c>HttpClient</c> is supplied (it then falls back to the
    /// <c>HttpClient.BaseAddress</c>).
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>The wire protocol; defaults to the service's primary declared protocol.</summary>
    public IProtocol? Protocol { get; set; }

    /// <summary>Protocol-agnostic hooks for observing and modifying client execution.</summary>
    public IList<IClientInterceptor> Interceptors { get; } = [];

    /// <summary>Runtime-owned retry policy. Null disables runtime retries.</summary>
    public ISmithyRetryStrategy? RetryStrategy { get; set; }

    /// <summary>
    /// Deadline for one operation execution, spanning all retry attempts and backoff delays.
    /// When exceeded the call throws <see cref="TimeoutException"/>. Null (the default) means no
    /// runtime-imposed deadline; the caller's <see cref="CancellationToken"/> always applies.
    /// </summary>
    public TimeSpan? OperationTimeout { get; set; }

    /// <summary>
    /// Configured auth schemes. The resolver installs the first scheme the service models for which
    /// a matching scheme is configured here; an empty list means anonymous access.
    /// </summary>
    public IList<ISmithyAuthScheme> AuthSchemes { get; } = [];

    /// <summary>Overrides the idempotency-token generator (defaults to a random GUID).</summary>
    public Func<string>? IdempotencyTokenProvider { get; set; }
}
