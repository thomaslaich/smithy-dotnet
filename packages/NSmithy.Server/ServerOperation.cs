using NSmithy.Core;
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

    /// <summary>
    /// The operation's effective auth schemes in priority order, as Smithy resolves them from the
    /// service's auth traits and the operation's <c>@auth</c>. Contains
    /// <see cref="ServerOperation.NoAuth"/> when the operation can be invoked without
    /// authentication: when no scheme applies, or it has <c>@optionalAuth</c>.
    /// </summary>
    IReadOnlyList<ShapeId> AuthSchemes { get; }

    /// <summary>Whether every call must authenticate with one of <see cref="AuthSchemes"/>.</summary>
    bool RequiresAuthentication { get; }

    /// <summary>Binds the operation to <paramref name="protocol"/>, once per mapped protocol.</summary>
    IServerOperationBinding Bind(IServiceProtocol protocol);

    /// <summary>
    /// Invokes the operation with an already-decoded <paramref name="input"/> through
    /// <paramref name="runtime"/>, which validates it and calls the handler resolved from
    /// <paramref name="services"/>, the services of the request being served.
    /// </summary>
    Task<object?> InvokeAsync(
        SmithyServerRuntime runtime,
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
    /// <summary><c>smithy.api#noAuth</c>: the scheme of calls made without authentication.</summary>
    public static readonly ShapeId NoAuth = new("smithy.api", "noAuth");

    /// <summary>
    /// An operation whose handler <paramref name="resolveHandler"/> resolves from each request's
    /// services, so handler lifetimes are the application's. Without
    /// <paramref name="authSchemes"/>, the operation needs no authentication.
    /// </summary>
    public static ServerOperation<TInput, TOutput> Create<TInput, TOutput>(
        OperationSchema<TInput, TOutput> schema,
        Func<IServiceProvider, Func<TInput, CancellationToken, Task<TOutput>>> resolveHandler,
        OperationJsonSchemas? jsonSchemas = null,
        IReadOnlyList<ShapeId>? authSchemes = null
    ) => new(schema, resolveHandler, jsonSchemas, authSchemes);
}

/// <summary>
/// A server operation that keeps its input and output types, so binding and dispatch need no
/// reflection.
/// </summary>
public sealed class ServerOperation<TInput, TOutput>(
    OperationSchema<TInput, TOutput> schema,
    Func<IServiceProvider, Func<TInput, CancellationToken, Task<TOutput>>> resolveHandler,
    OperationJsonSchemas? jsonSchemas = null,
    IReadOnlyList<ShapeId>? authSchemes = null
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

    public IReadOnlyList<ShapeId> AuthSchemes { get; } = CopyAuthSchemes(authSchemes);

    public bool RequiresAuthentication => !AuthSchemes.Contains(ServerOperation.NoAuth);

    // For invocation with decoded input; HTTP dispatch validates through its protocol instead.
    private readonly ISmithyValidator<TInput>? inputValidator = SmithyValidator.FromSchema(
        schema.Input
    );

    // An empty list would mean neither authenticated nor anonymous calls can reach the operation.
    private static ShapeId[] CopyAuthSchemes(IReadOnlyList<ShapeId>? authSchemes) =>
        authSchemes switch
        {
            null => [ServerOperation.NoAuth],
            [] => throw new ArgumentException(
                $"An operation needs at least one auth scheme; use {nameof(ServerOperation)}.{nameof(ServerOperation.NoAuth)} for none.",
                nameof(authSchemes)
            ),
            _ => [.. authSchemes],
        };

    public IServerOperationBinding Bind(IServiceProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        return new Binding(Schema, protocol.ForServerOperation(Schema), resolveHandler);
    }

    public async Task<object?> InvokeAsync(
        SmithyServerRuntime runtime,
        object input,
        IServiceProvider services,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(runtime);
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

        return await runtime
            .InvokeAsync(inputValidator, typedInput, resolveHandler(services), cancellationToken)
            .ConfigureAwait(false);
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
