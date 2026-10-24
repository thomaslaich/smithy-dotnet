using NSmithy.Core.Serde;
using NSmithy.Http;

namespace NSmithy.Server;

/// <summary>
/// An operation a generated server exposes: its schema and how to resolve its handler for a
/// request. Host adapters bind it to each protocol a service is mapped over.
/// </summary>
public interface IServerOperation
{
    IOperationSchema Schema { get; }

    /// <summary>Binds the operation to <paramref name="protocol"/>, once per mapped protocol.</summary>
    IServerOperationBinding Bind(IServiceProtocol protocol);
}

/// <summary>An operation bound to one protocol, ready to dispatch requests.</summary>
public interface IServerOperationBinding
{
    IOperationSchema Schema { get; }

    /// <inheritdoc cref="IServerOperationProtocol{TInput, TOutput}.StreamsRequestBody"/>
    bool StreamsRequestBody { get; }

    /// <summary>
    /// Dispatches <paramref name="request"/> to the handler resolved from
    /// <paramref name="services"/>, the request's services.
    /// </summary>
    Task<SmithyHttpServerResponse> DispatchAsync(
        SmithyServerRuntime runtime,
        SmithyHttpRequest request,
        IServiceProvider services,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Creates server operations with inferred input and output types.</summary>
public static class ServerOperation
{
    /// <summary>
    /// An operation whose handler <paramref name="resolveHandler"/> resolves from each request's
    /// services, so handler lifetimes are the application's.
    /// </summary>
    public static ServerOperation<TInput, TOutput> Create<TInput, TOutput>(
        OperationSchema<TInput, TOutput> schema,
        Func<IServiceProvider, Func<TInput, CancellationToken, Task<TOutput>>> resolveHandler
    ) => new(schema, resolveHandler);
}

/// <summary>
/// A server operation that keeps its input and output types, so binding and dispatch need no
/// reflection.
/// </summary>
public sealed class ServerOperation<TInput, TOutput>(
    OperationSchema<TInput, TOutput> schema,
    Func<IServiceProvider, Func<TInput, CancellationToken, Task<TOutput>>> resolveHandler
) : IServerOperation
{
    private readonly Func<
        IServiceProvider,
        Func<TInput, CancellationToken, Task<TOutput>>
    > resolveHandler = resolveHandler ?? throw new ArgumentNullException(nameof(resolveHandler));

    public OperationSchema<TInput, TOutput> Schema { get; } =
        schema ?? throw new ArgumentNullException(nameof(schema));

    IOperationSchema IServerOperation.Schema => Schema;

    public IServerOperationBinding Bind(IServiceProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        return new Binding(Schema, protocol.ForServerOperation(Schema), resolveHandler);
    }

    private sealed class Binding(
        OperationSchema<TInput, TOutput> schema,
        IServerOperationProtocol<TInput, TOutput> protocol,
        Func<IServiceProvider, Func<TInput, CancellationToken, Task<TOutput>>> resolveHandler
    ) : IServerOperationBinding
    {
        public IOperationSchema Schema => schema;

        public bool StreamsRequestBody => protocol.StreamsRequestBody;

        public Task<SmithyHttpServerResponse> DispatchAsync(
            SmithyServerRuntime runtime,
            SmithyHttpRequest request,
            IServiceProvider services,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(runtime);
            ArgumentNullException.ThrowIfNull(services);
            return runtime.DispatchAsync(
                protocol,
                request,
                resolveHandler(services),
                cancellationToken
            );
        }
    }
}
