using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Server;

/// <summary>
/// An operation schema bound to its server handler. The object-based invocation seam lets
/// protocol-neutral adapters dispatch an operation after constructing input from its runtime
/// schema; the generic implementation retains type safety at the handler boundary.
/// </summary>
public interface IServiceOperation
{
    IOperationSchema Schema { get; }

    Task<object?> InvokeAsync(
        object input,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Creates typed service-operation bindings with inferred input and output types.</summary>
public static class ServiceOperation
{
    public static ServiceOperation<TInput, TOutput> Create<TInput, TOutput>(
        OperationSchema<TInput, TOutput> schema,
        Func<TInput, CancellationToken, Task<TOutput>> handler
    ) => new(schema, handler);
}

/// <summary>A type-safe operation binding exposed through <see cref="IServiceOperation"/>.</summary>
public sealed class ServiceOperation<TInput, TOutput> : IServiceOperation
{
    private readonly Func<TInput, CancellationToken, Task<TOutput>> handler;

    public ServiceOperation(
        OperationSchema<TInput, TOutput> schema,
        Func<TInput, CancellationToken, Task<TOutput>> handler
    )
    {
        Schema = schema ?? throw new ArgumentNullException(nameof(schema));
        this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public OperationSchema<TInput, TOutput> Schema { get; }

    IOperationSchema IServiceOperation.Schema => Schema;

    public async Task<object?> InvokeAsync(
        object input,
        CancellationToken cancellationToken = default
    )
    {
        if (input is not TInput typedInput)
        {
            var actualType = input is null ? "<null>" : input.GetType().FullName;
            throw new ArgumentException(
                $"Operation '{Schema.Id}' expects input assignable to "
                    + $"'{typeof(TInput).FullName}', but received '{actualType}'.",
                nameof(input)
            );
        }

        return await handler(typedInput, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// The protocol- and host-neutral executable surface of a generated Smithy service.
/// </summary>
public sealed class ServiceOperationCatalog
{
    private readonly ReadOnlyDictionary<ShapeId, IServiceOperation> operationsById;

    public ServiceOperationCatalog(ServiceSchema schema, params IServiceOperation[] operations)
    {
        Schema = schema ?? throw new ArgumentNullException(nameof(schema));
        ArgumentNullException.ThrowIfNull(operations);

        var copied = operations.ToArray();
        var byId = new Dictionary<ShapeId, IServiceOperation>();
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
        operationsById = new ReadOnlyDictionary<ShapeId, IServiceOperation>(byId);
    }

    public ServiceSchema Schema { get; }

    public IReadOnlyList<IServiceOperation> Operations { get; }

    public IServiceOperation GetOperation(ShapeId id) =>
        operationsById.TryGetValue(id, out var operation)
            ? operation
            : throw new KeyNotFoundException(
                $"Service '{Schema.Id}' does not contain operation '{id}'."
            );

    public bool TryGetOperation(
        ShapeId id,
        [NotNullWhen(true)] out IServiceOperation? operation
    ) => operationsById.TryGetValue(id, out operation);
}
