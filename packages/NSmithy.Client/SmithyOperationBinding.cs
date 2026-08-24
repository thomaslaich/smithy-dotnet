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
        IReadOnlyList<string>? authSchemeIds = null,
        Func<TInput, string>? hostPrefix = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId.Name, nameof(serviceId));
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId.Name, nameof(operationId));
        ArgumentNullException.ThrowIfNull(protocol);

        ServiceId = serviceId;
        OperationId = operationId;
        Protocol = protocol;
        AuthSchemeIds = authSchemeIds ?? [];
        HostPrefix = hostPrefix;
        ActivityName = $"{serviceId.Name}.{operationId.Name}";
        ServiceIdTag = serviceId.ToString();
    }

    public ShapeId ServiceId { get; }

    public ShapeId OperationId { get; }

    public IClientOperationProtocol<TInput, TOutput> Protocol { get; }

    /// <summary>
    /// The span name for this operation, built once here rather than interpolated per invocation.
    /// <see cref="System.Diagnostics.ActivitySource.StartActivity(string, System.Diagnostics.ActivityKind)"/>
    /// returns null when nothing is subscribed, but its argument is evaluated either way, so
    /// interpolating at the call site allocated a string on every call to hand to a method that
    /// usually discards it.
    /// </summary>
    internal string ActivityName { get; }

    /// <summary>
    /// The <c>rpc.service</c> metric dimension. <see cref="ShapeId.ToString"/> allocates, and the
    /// value is constant per binding, so it is materialized once with the binding.
    /// </summary>
    internal string ServiceIdTag { get; }

    /// <summary>
    /// The operation's effective modeled auth scheme ids in Smithy priority order — the
    /// service's effective schemes, overridden by a per-operation <c>@auth</c> trait. Empty for
    /// anonymous operations.
    /// </summary>
    public IReadOnlyList<string> AuthSchemeIds { get; }

    /// <summary>
    /// Expands this operation's modeled endpoint host prefix from its typed input, or null when the
    /// operation has no <c>@endpoint</c> trait.
    /// </summary>
    public Func<TInput, string>? HostPrefix { get; }
}
