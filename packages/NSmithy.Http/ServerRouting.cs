using NSmithy.Core;

namespace NSmithy.Http;

/// <summary>
/// How requests reach a service's operations over one protocol. A protocol reports it through
/// <see cref="IServiceProtocol.GetServerRouting"/>; a host adapter turns it into endpoints.
/// </summary>
public abstract record ServerRouting
{
    private protected ServerRouting() { }

    /// <summary>Routing for a protocol that does not serve requests.</summary>
    public static ServerRouting None { get; } = new ByRoute([]);

    /// <summary>Each operation has its own route, matched by method and path.</summary>
    public sealed record ByRoute(IReadOnlyList<ServerRoute> Routes) : ServerRouting;

    /// <summary>
    /// One route for the service. The protocol names the operation from the request, which the
    /// host buffers so the operation's protocol deserializes the same bytes.
    /// </summary>
    /// <param name="Method">The HTTP method of the route.</param>
    /// <param name="Path">The path of the route.</param>
    /// <param name="ResolveOperation">The operation a request is for, or null when it names none.</param>
    /// <param name="UnknownOperation">The response to a request that names no operation.</param>
    public sealed record ByDispatch(
        string Method,
        string Path,
        Func<SmithyHttpRequest, CancellationToken, ValueTask<ShapeId?>> ResolveOperation,
        Func<SmithyHttpRequest, SmithyHttpServerResponse> UnknownOperation
    ) : ServerRouting;
}

/// <summary>The route of one operation.</summary>
/// <param name="Operation">The operation the route reaches.</param>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">
/// The path pattern, with <c>{label}</c> segments and a greedy <c>{label+}</c> segment as in
/// Smithy's <c>@http</c> URIs.
/// </param>
/// <param name="RequiredQuery">
/// Literal query parameters the request must carry, with a null value when only the name must be
/// present.
/// </param>
public sealed record ServerRoute(
    ShapeId Operation,
    string Method,
    string Path,
    IReadOnlyList<KeyValuePair<string, string?>> RequiredQuery
);
