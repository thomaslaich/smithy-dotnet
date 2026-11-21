using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Http;

namespace NSmithy.Server;

/// <summary>
/// The protocol- and host-neutral executable surface of a generated Smithy service: its operation
/// table, which every host adapter works from.
/// </summary>
public sealed class ServiceOperationCatalog
{
    private readonly ReadOnlyDictionary<ShapeId, IServerOperation> operationsById;
    private readonly Dictionary<ShapeId, ServerOperationIdentity> identities = [];

    public ServiceOperationCatalog(ServiceSchema schema, params IServerOperation[] operations)
    {
        Schema = schema ?? throw new ArgumentNullException(nameof(schema));
        ArgumentNullException.ThrowIfNull(operations);

        var copied = operations.ToArray();
        var byId = new Dictionary<ShapeId, IServerOperation>();
        foreach (var operation in copied)
        {
            ArgumentNullException.ThrowIfNull(operation);
            if (!byId.TryAdd(operation.Schema.Id, operation))
            {
                throw new ArgumentException(
                    $"Operation '{operation.Schema.Id}' is registered more than once.",
                    nameof(operations)
                );
            }

            identities.Add(
                operation.Schema.Id,
                new ServerOperationIdentity(schema, operation.Schema)
            );
        }

        Operations = Array.AsReadOnly(copied);
        operationsById = new ReadOnlyDictionary<ShapeId, IServerOperation>(byId);
    }

    public ServiceSchema Schema { get; }

    public IReadOnlyList<IServerOperation> Operations { get; }

    public IServerOperation GetOperation(ShapeId id) =>
        operationsById.TryGetValue(id, out var operation)
            ? operation
            : throw new KeyNotFoundException(
                $"Service '{Schema.Id}' does not contain operation '{id}'."
            );

    public bool TryGetOperation(ShapeId id, [NotNullWhen(true)] out IServerOperation? operation) =>
        operationsById.TryGetValue(id, out operation);

    /// <summary>
    /// Binds every operation to <paramref name="protocol"/> for this service, once per mapped
    /// protocol, keyed by operation id.
    /// </summary>
    public IReadOnlyDictionary<ShapeId, IServerOperationBinding> Bind(IProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        var serviceProtocol = protocol.ForService(Schema);
        return new ReadOnlyDictionary<ShapeId, IServerOperationBinding>(
            Operations.ToDictionary(
                operation => operation.Schema.Id,
                operation => operation.Bind(identities[operation.Schema.Id], serviceProtocol)
            )
        );
    }

    /// <summary>
    /// Invokes <paramref name="operation"/> with an already-decoded <paramref name="input"/>
    /// through <paramref name="runtime"/>, which validates it and calls the handler resolved from
    /// <paramref name="services"/>, the services of the request being served. Adapters without a
    /// wire request use this, such as MCP after decoding tool arguments.
    /// </summary>
    public Task<object?> InvokeAsync(
        ShapeId operation,
        SmithyServerRuntime runtime,
        object input,
        IServiceProvider services,
        CancellationToken cancellationToken = default
    ) =>
        GetOperation(operation)
            .InvokeAsync(Identity(operation), runtime, input, services, cancellationToken);

    internal ServerOperationIdentity Identity(ShapeId operation) =>
        identities.TryGetValue(operation, out var identity)
            ? identity
            : throw new KeyNotFoundException(
                $"Service '{Schema.Id}' does not contain operation '{operation}'."
            );
}
