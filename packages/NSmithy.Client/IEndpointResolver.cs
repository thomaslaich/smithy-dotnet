using NSmithy.Core;

namespace NSmithy.Client;

/// <summary>
/// Resolves the effective endpoint for one operation execution. Resolution runs once per
/// invocation, before serialization, and may do I/O (endpoint discovery). A static
/// <c>Config.Endpoint</c> is the simplest resolver (<see cref="StaticEndpointResolver"/>) and is
/// used when no resolver is configured.
/// </summary>
public interface IEndpointResolver
{
    ValueTask<SmithyEndpoint> ResolveEndpointAsync(
        SmithyEndpointParameters parameters,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// The endpoint this resolver returns for every operation regardless of its parameters, or null
    /// when resolution genuinely depends on them. Defaulted to null, so existing resolvers keep
    /// working unchanged.
    /// </summary>
    /// <remarks>
    /// A resolver that answers here lets the runtime skip building a
    /// <see cref="SmithyEndpointParameters"/> it would only discard — the common case, since the
    /// overwhelming majority of clients are configured with one fixed endpoint. Only return a value
    /// if the result truly does not vary by service, operation, or input.
    /// </remarks>
    SmithyEndpoint? StaticEndpoint => null;
}

/// <summary>
/// A resolved endpoint: the base URI serialized request paths resolve against, headers to add to
/// every request sent to it, and an optional narrowing of the auth schemes usable against it
/// (ordered subset semantics: when non-null, only modeled schemes also present here are
/// considered by auth selection).
/// </summary>
public sealed record SmithyEndpoint(
    Uri Uri,
    IReadOnlyDictionary<string, string>? Headers = null,
    IReadOnlyList<string>? AuthSchemes = null
)
{
    public Uri Uri { get; } =
        Uri.IsAbsoluteUri
            ? Uri
            : throw new ArgumentException("Endpoint URI must be absolute.", nameof(Uri));
}

/// <summary>
/// What an <see cref="IEndpointResolver"/> sees: the operation's Smithy identifiers, the
/// statically configured endpoint (if any), and the typed operation input (for host-label-style
/// resolution).
/// </summary>
public sealed record SmithyEndpointParameters(
    ShapeId ServiceId,
    ShapeId OperationId,
    Uri? ConfiguredEndpoint,
    object? Input
);

/// <summary>Resolves every operation to one fixed endpoint.</summary>
public sealed class StaticEndpointResolver(Uri uri) : IEndpointResolver
{
    private readonly SmithyEndpoint endpoint = new(
        uri ?? throw new ArgumentNullException(nameof(uri))
    );

    public ValueTask<SmithyEndpoint> ResolveEndpointAsync(
        SmithyEndpointParameters parameters,
        CancellationToken cancellationToken = default
    ) => ValueTask.FromResult(endpoint);

    public SmithyEndpoint? StaticEndpoint => endpoint;
}
