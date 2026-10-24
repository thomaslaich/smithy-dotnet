using NSmithy.Core.Serde;
using NSmithy.Core.Validation;
using NSmithy.Http;

namespace NSmithy.Server;

/// <summary>
/// An operation a generated server exposes: its schema and how to resolve its handler for a
/// request. HTTP routing binds it to each protocol a service is mapped over; adapters that hold the
/// input as an object, such as MCP, invoke it directly.
/// </summary>
public interface IServerOperation
{
    IOperationSchema Schema { get; }

    /// <summary>
    /// Canonical JSON Schema documents for the operation's input and output, used by MCP tools.
    /// Null for streaming operations.
    /// </summary>
    OperationJsonSchemas? JsonSchemas { get; }

    /// <summary>Binds the operation to <paramref name="protocol"/>, once per mapped protocol.</summary>
    IServerOperationBinding Bind(IServiceProtocol protocol);

    /// <summary>
    /// Validates <paramref name="input"/> against the operation's input schema and calls the
    /// handler resolved from <paramref name="services"/>, the services of the request being served.
    /// </summary>
    Task<object?> InvokeAsync(
        object input,
        IServiceProvider services,
        CancellationToken cancellationToken = default
    );
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
        Func<IServiceProvider, Func<TInput, CancellationToken, Task<TOutput>>> resolveHandler,
        OperationJsonSchemas? jsonSchemas = null
    ) => new(schema, resolveHandler, jsonSchemas);
}

/// <summary>
/// A server operation that keeps its input and output types, so binding and dispatch need no
/// reflection.
/// </summary>
public sealed class ServerOperation<TInput, TOutput>(
    OperationSchema<TInput, TOutput> schema,
    Func<IServiceProvider, Func<TInput, CancellationToken, Task<TOutput>>> resolveHandler,
    OperationJsonSchemas? jsonSchemas = null
) : IServerOperation
{
    private readonly Func<
        IServiceProvider,
        Func<TInput, CancellationToken, Task<TOutput>>
    > resolveHandler = resolveHandler ?? throw new ArgumentNullException(nameof(resolveHandler));

    public OperationSchema<TInput, TOutput> Schema { get; } =
        schema ?? throw new ArgumentNullException(nameof(schema));

    IOperationSchema IServerOperation.Schema => Schema;

    public OperationJsonSchemas? JsonSchemas { get; } = jsonSchemas;

    // For direct invocation; HTTP dispatch validates through its protocol instead.
    private readonly ISmithyValidator<TInput>? inputValidator = SmithyValidator.FromSchema(
        schema.Input
    );

    public IServerOperationBinding Bind(IServiceProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        return new Binding(Schema, protocol.ForServerOperation(Schema), resolveHandler);
    }

    public async Task<object?> InvokeAsync(
        object input,
        IServiceProvider services,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        if (input is not TInput typedInput)
        {
            var actualType = input is null ? "<null>" : input.GetType().FullName;
            throw new ArgumentException(
                $"Operation '{Schema.Id}' expects input assignable to "
                    + $"'{typeof(TInput).FullName}', but received '{actualType}'.",
                nameof(input)
            );
        }

        inputValidator?.Validate(typedInput);
        return await resolveHandler(services)(typedInput, cancellationToken).ConfigureAwait(false);
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
