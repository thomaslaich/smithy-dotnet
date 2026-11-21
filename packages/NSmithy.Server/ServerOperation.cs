using System.Diagnostics;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Core.Validation;
using NSmithy.Http;

namespace NSmithy.Server;

/// <summary>
/// An operation a generated server exposes: its schema and how to resolve its handler for a
/// request. Hosts bind it to each protocol a service is mapped over, or invoke it with decoded
/// input, through the service's <see cref="ServiceOperationCatalog"/>.
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

    /// <summary>
    /// The services, any one of which serves the operation, so mapping can fail when none is
    /// registered. Empty when the operation does not say.
    /// </summary>
    IReadOnlyList<Type> HandlerTypes => [];

    // Bound and invoked through ServiceOperationCatalog, which knows the service a call is for.
    internal IServerOperationBinding Bind(
        ServerOperationIdentity identity,
        IServiceProtocol protocol
    );

    internal Task<object?> InvokeAsync(
        ServerOperationIdentity identity,
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

    internal ServerOperationIdentity Identity { get; }

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
    /// <paramref name="handlerTypes"/> are the services, any one of which serves it.
    /// </summary>
    public static ServerOperation<TInput, TOutput> Create<TInput, TOutput>(
        OperationSchema<TInput, TOutput> schema,
        Func<IServiceProvider, Func<TInput, CancellationToken, Task<TOutput>>> resolveHandler,
        OperationJsonSchemas? jsonSchemas = null,
        IReadOnlyList<ShapeId>? authSchemes = null,
        IReadOnlyList<Type>? handlerTypes = null
    ) => new(schema, resolveHandler, jsonSchemas, authSchemes, handlerTypes);
}

/// <summary>
/// A server operation that keeps its input and output types, so binding and dispatch need no
/// reflection.
/// </summary>
public sealed class ServerOperation<TInput, TOutput>(
    OperationSchema<TInput, TOutput> schema,
    Func<IServiceProvider, Func<TInput, CancellationToken, Task<TOutput>>> resolveHandler,
    OperationJsonSchemas? jsonSchemas = null,
    IReadOnlyList<ShapeId>? authSchemes = null,
    IReadOnlyList<Type>? handlerTypes = null
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

    public IReadOnlyList<Type> HandlerTypes { get; } = [.. handlerTypes ?? []];

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

    IServerOperationBinding IServerOperation.Bind(
        ServerOperationIdentity identity,
        IServiceProtocol protocol
    )
    {
        ArgumentNullException.ThrowIfNull(protocol);
        return new Binding(this, identity, protocol.ForServerOperation(Schema));
    }

    async Task<object?> IServerOperation.InvokeAsync(
        ServerOperationIdentity identity,
        SmithyServerRuntime runtime,
        object input,
        IServiceProvider services,
        CancellationToken cancellationToken
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
            .InvokeAsync(inputValidator, typedInput, Handler(identity, services), cancellationToken)
            .ConfigureAwait(false);
    }

    // The handler for one call, in a span when an application subscribes to the source.
    private Func<TInput, CancellationToken, Task<TOutput>> Handler(
        ServerOperationIdentity identity,
        IServiceProvider services
    )
    {
        var handler = resolveHandler(services);
        return SmithyServerTelemetry.ActivitySource.HasListeners()
            ? (input, cancellationToken) =>
                ObserveAsync(identity, handler, input, cancellationToken)
            : handler;
    }

    private static async Task<TOutput> ObserveAsync(
        ServerOperationIdentity identity,
        Func<TInput, CancellationToken, Task<TOutput>> handler,
        TInput input,
        CancellationToken cancellationToken
    )
    {
        using var activity = SmithyServerTelemetry.ActivitySource.StartActivity(
            identity.ActivityName,
            ActivityKind.Internal
        );
        if (activity is { IsAllDataRequested: true })
        {
            SmithyServerTelemetry.Describe(activity, identity);
        }

        Task<TOutput> invocation;
        try
        {
            invocation = handler(input, cancellationToken);
        }
        catch (Exception exception)
        {
            Fail(activity, identity, exception);
            throw;
        }

        // Read off the task, as the runtime does, so a modeled error is not thrown twice here.
        await ((Task)invocation).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (invocation.Exception?.InnerExceptions is [var raised])
        {
            Fail(activity, identity, raised);
        }

        return await invocation.ConfigureAwait(false);
    }

    // A modeled client error is the caller's mistake, so only a fault marks the span.
    private static void Fail(
        Activity? activity,
        ServerOperationIdentity identity,
        Exception exception
    )
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag("error.type", SmithyServerTelemetry.ErrorType(exception));
        if (identity.IsFault(exception))
        {
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        }
    }

    private sealed class Binding(
        ServerOperation<TInput, TOutput> operation,
        ServerOperationIdentity identity,
        IServerOperationProtocol<TInput, TOutput> protocol
    ) : IServerOperationBinding
    {
        public IOperationSchema Schema => operation.Schema;

        ServerOperationIdentity IServerOperationBinding.Identity => identity;

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
                operation.Handler(identity, services),
                cancellationToken
            );
        }
    }
}
