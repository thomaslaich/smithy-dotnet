using NSmithy.Core.Serde;
using NSmithy.Server;

namespace NSmithy.Server.AspNetCore;

/// <summary>
/// Endpoint metadata on every endpoint a Smithy service maps: the service, the protocol, and the
/// operations the endpoint serves. A route endpoint serves one operation; an endpoint of a
/// protocol that resolves the operation from the request serves all of them.
/// </summary>
public sealed class SmithyEndpointMetadata(
    ServiceSchema service,
    string protocol,
    IReadOnlyList<IServerOperation> operations
)
{
    public ServiceSchema Service { get; } =
        service ?? throw new ArgumentNullException(nameof(service));

    /// <summary>The protocol's name, as in <see cref="SmithyServerProtocol.Name"/>.</summary>
    public string Protocol { get; } = protocol ?? throw new ArgumentNullException(nameof(protocol));

    public IReadOnlyList<IServerOperation> Operations { get; } =
        operations ?? throw new ArgumentNullException(nameof(operations));
}
