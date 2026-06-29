using NSmithy.Http;

namespace NSmithy.Client;

/// <summary>
/// Common, protocol-agnostic client configuration. Generated clients take a service-specific
/// subclass (<c>{Service}ClientConfig</c>) so service-specific options can be added later without
/// changing the client constructor surface.
/// </summary>
public class SmithyClientConfig
{
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
    /// Configured auth schemes. The resolver installs the first scheme the service models for which
    /// a matching scheme is configured here; an empty list means anonymous access.
    /// </summary>
    public IList<ISmithyAuthScheme> AuthSchemes { get; } = [];

    /// <summary>Overrides the idempotency-token generator (defaults to a random GUID).</summary>
    public Func<string>? IdempotencyTokenProvider { get; set; }
}
