using NSmithy.Core;
using NSmithy.Core.Serde;

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
    private Uri uri = RequireAbsolute(Uri);

    /// <summary>The absolute base URI.</summary>
    public Uri Uri
    {
        get => uri;
        init => uri = RequireAbsolute(value);
    }

    private static Uri RequireAbsolute(Uri value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.IsAbsoluteUri
            ? value
            : throw new ArgumentException("Endpoint URI must be absolute.", nameof(value));
    }
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
)
{
    /// <summary>
    /// The modeled operation, including input/output schemas and traits. Generated clients always
    /// supply it; manually constructed operation bindings may omit it. Schema visitors recover
    /// typed input metadata without reflection. This is not a bag of bound endpoint-rule parameters.
    /// </summary>
    public IOperationSchema? Operation { get; init; }
}

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
