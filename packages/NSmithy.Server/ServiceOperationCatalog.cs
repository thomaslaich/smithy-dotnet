using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Server;

/// <summary>
/// The protocol- and host-neutral executable surface of a generated Smithy service: its operation
/// table, which every host adapter works from.
/// </summary>
public sealed class ServiceOperationCatalog
{
    private readonly ReadOnlyDictionary<ShapeId, IServerOperation> operationsById;

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
}
