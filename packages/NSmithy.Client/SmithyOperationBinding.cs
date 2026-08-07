using NSmithy.Core;
using NSmithy.Http;

namespace NSmithy.Client;

/// <summary>
/// A precomputed operation handle: the Smithy identifiers plus the operation-bound protocol.
/// Generated clients build one binding per unary operation at construction and pass it to
/// <see cref="SmithyClientRuntime.InvokeAsync{TInput, TOutput}"/> on every call. Pure data —
/// request-mutating traits (<c>@requestCompression</c>, <c>@httpChecksumRequired</c>) are applied
/// by the protocol during serialization.
/// </summary>
public sealed class SmithyOperationBinding<TInput, TOutput>
{
    public SmithyOperationBinding(
        ShapeId serviceId,
        ShapeId operationId,
        IClientOperationProtocol<TInput, TOutput> protocol,
        IReadOnlyList<string>? authSchemeIds = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId.Name, nameof(serviceId));
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId.Name, nameof(operationId));
        ArgumentNullException.ThrowIfNull(protocol);

        ServiceId = serviceId;
        OperationId = operationId;
        Protocol = protocol;
        AuthSchemeIds = authSchemeIds ?? [];
    }

    public ShapeId ServiceId { get; }

    public ShapeId OperationId { get; }

    public IClientOperationProtocol<TInput, TOutput> Protocol { get; }

    /// <summary>
    /// The operation's effective modeled auth scheme ids in Smithy priority order — the
    /// service's effective schemes, overridden by a per-operation <c>@auth</c> trait. Empty for
    /// anonymous operations.
    /// </summary>
    public IReadOnlyList<string> AuthSchemeIds { get; }
}
