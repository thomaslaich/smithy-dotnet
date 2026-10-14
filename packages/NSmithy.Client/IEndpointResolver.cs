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
/// every request sent to it, an optional narrowing of the auth schemes usable against it (ordered
/// subset semantics: when non-null, only modeled schemes also present here are considered by auth
/// selection, and the selected scheme's signer reads its entry's properties), and any other
/// properties the endpoint rules attached.
/// </summary>
public sealed record SmithyEndpoint(
    Uri Uri,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Headers = null,
    IReadOnlyList<SmithyEndpointAuthScheme>? AuthSchemes = null,
    IReadOnlyDictionary<string, Document>? Properties = null
)
{
    public Uri Uri { get; } =
        Uri.IsAbsoluteUri
            ? Uri
            : throw new ArgumentException("Endpoint URI must be absolute.", nameof(Uri));
}

/// <summary>
/// An auth scheme a resolved endpoint supports, keyed by the auth trait's shape id (for example
/// <c>aws.auth#sigv4</c>), with the scheme properties the endpoint sets (for SigV4,
/// <c>signingName</c> and <c>signingRegion</c>).
/// </summary>
public sealed record SmithyEndpointAuthScheme(
    string SchemeId,
    IReadOnlyDictionary<string, Document>? Properties = null
)
{
    public string SchemeId { get; } =
        string.IsNullOrWhiteSpace(SchemeId)
            ? throw new ArgumentException("Auth scheme id must be set.", nameof(SchemeId))
            : SchemeId;

    /// <summary>The string property <paramref name="name"/>, or null when it is not set.</summary>
    public string? GetString(string name) =>
        Properties is not null
        && Properties.TryGetValue(name, out var value)
        && value.Kind == DocumentKind.String
            ? value.AsString()
            : null;

    /// <summary>The boolean property <paramref name="name"/>, or null when it is not set.</summary>
    public bool? GetBoolean(string name) =>
        Properties is not null
        && Properties.TryGetValue(name, out var value)
        && value.Kind == DocumentKind.Boolean
            ? value.AsBoolean()
            : null;
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
