# HTTP Architecture Improvements

Potential simplifications to the generated HTTP client/server surface and its
runtime boundary.

## Status

The first two changes are implemented: `SmithyHttpClientEnvironment` centralizes
client construction and ownership, and generated endpoints resolve
`SmithyServerRuntime` through DI. The sections below retain the motivation and
proposed alternatives; endpoint descriptors and HTTP-specific renames remain
deferred.
The current HTTP architecture is sound: operation methods and endpoints are
thin, protocol bindings own wire behavior, and shared runtimes own the client
and server execution algorithms. The improvements below preserve that design
while reducing construction and hosting code emitted per service.

## Existing Strengths

The current client path has a clear separation:

```text
generated client method
        |
        v
SmithyOperationBinding<TInput, TOutput>
        |
        v
IClientOperationProtocol<TInput, TOutput>
        |
        v
SmithyClientRuntime
        |
        v
IHttpTransport
```

Generated client methods contain almost no behavior. They validate the direct
argument shape, apply generated idempotency-token defaults when required, and
invoke the runtime with a precomputed operation binding. Protocol adapters own
serialization and error discrimination. The runtime owns endpoint resolution,
authentication, retries, interceptors, telemetry, and transport invocation.

The server is the corresponding inverse:

```text
generated ASP.NET Core endpoint
        |
        v
SmithyAspNetCoreHost
        |
        v
SmithyServerRuntime
        |
        +-- deserialize and validate through the operation protocol
        +-- invoke the typed handler
        +-- serialize output or a modeled error
```

These are the important architectural properties to retain:

- handler and client interfaces derive from the model, not from a protocol;
- protocols bind once at service and operation construction boundaries;
- generated operation methods do not implement the execution lifecycle;
- the host adapter contains framework conversion but no protocol wire rules;
- neutral HTTP request and response types separate protocol and host packages;
- multiple protocols can use the same generated handlers.

## Design Rule

Generated code should contain contract-specific facts. Runtime libraries should
contain reusable behavior and state machines.

The current implementation follows this rule during invocation and dispatch,
but less completely during client construction and server endpoint mapping.

## 1. Centralize Client Construction

### Previous shape

Each generated client constructor previously assembled much of the runtime
environment:

- choose the configured or default protocol;
- create and configure an owned `HttpClient` when only an endpoint is supplied;
- choose the modeled HTTP version preference;
- bind the protocol to the service;
- resolve configured auth schemes;
- construct `HttpClientTransport` and `SmithyClientRuntime`;
- track whether the generated client owns the `HttpClient`;
- bind every operation.

The construction algorithm was repeated for every service. Changes to ownership,
default transport setup, or common config flow therefore required generator
changes and regenerated a substantial block in every consumer project.

### Implemented shape

Common construction now lives in a hand-written factory that returns a disposable
client environment:

```csharp
public sealed class SmithyHttpClientEnvironment : IDisposable
{
    public SmithyClientRuntime Runtime { get; }
    public IServiceProtocol ServiceProtocol { get; }

    public static SmithyHttpClientEnvironment Create(
        ServiceSchema service,
        SmithyClientConfig config,
        Func<IProtocol> defaultProtocol,
        IReadOnlyList<string> modeledAuthSchemes,
        SmithyHttpVersionPreference? modeledHttpVersion = null,
        HttpClient? httpClient = null);
}
```

One runtime type implements common endpoint/config/transport construction and
disposes only the resources it created.

Generated construction then becomes:

```csharp
environment = SmithyHttpClientEnvironment.Create(
    WeatherSchema.Schema,
    config,
    static () => new RestJson1Protocol(),
    ModeledAuthSchemes,
    WeatherHttpVersionPreference);

getForecastBinding = new(
    WeatherSchema.Id,
    GetForecastSchema.Id,
    environment.ServiceProtocol.ForClientOperation(GetForecastSchema.Schema));
```

Caller-supplied `HttpClient` and `SmithyClientRuntime` overloads flow through
`Create` and `FromRuntime`, respectively. Generated constructors dispose the
environment if operation binding throws.

### Benefits

- transport ownership is implemented and tested once;
- adding a common client option usually changes runtime code, not codegen;
- generated constructors become easier to inspect;
- service-specific code remains limited to defaults and operation bindings;
- direct construction and dependency-injection construction share one path.

## 2. Resolve the Server Runtime Through Dependency Injection

`SmithyAspNetCoreHost` previously owned a static default `SmithyServerRuntime`.
That is pleasantly small while the runtime is stateless, but it becomes the
wrong ownership boundary when server interceptors, telemetry, exception policy,
or other lifecycle configuration is added.

Generated endpoints now receive the runtime from ASP.NET Core dependency
injection and pass it to the host adapter:

```csharp
endpoints.MapPost("/weather", async (
    HttpContext context,
    [FromServices] SmithyServerRuntime runtime,
    IGetWeatherHandler handler,
    CancellationToken cancellationToken) =>
{
    await SmithyAspNetCoreHost.DispatchAsync(
        runtime,
        context,
        GetWeatherProtocol,
        handler.GetWeatherAsync,
        cancellationToken: cancellationToken);
});
```

The generated `Add{Service}Handler<THandler>` registration calls
`AddSmithyServer`, which uses `TryAddSingleton` to provide a default runtime.
Applications may register a runtime with another lifetime before this call.
Interceptors and telemetry remain future work. The host adapter owns only
ASP.NET Core request/response conversion.

This is preferable to placing configurable global state on
`SmithyAspNetCoreHost` or adding server-policy parameters to every generated map
method.

## 3. Consider Descriptor-Driven Endpoint Mapping

### Current shape

Generated server code emits one ASP.NET Core mapping block per operation and
protocol. It also emits:

- precomputed server operation protocol fields;
- route collision checks;
- static-query-literal guards;
- typed handler adapters for unit and streaming shapes;
- selection logic for the generated protocol flags enum.

The dispatch algorithm is already shared, so this code is not fundamentally
misplaced. It is nevertheless procedural code repeated across services.

### Proposed direction

Generate immutable HTTP endpoint descriptors and let the ASP.NET Core adapter
iterate and map them:

```csharp
internal static readonly SmithyHttpEndpointDefinition<
    GetWeatherInput,
    GetWeatherOutput> GetWeatherRestJson1 =
        new(
            operation: GetWeatherSchema.Schema,
            method: "GET",
            route: "/weather/{city}",
            protocol: GetWeatherProtocol,
            handler: static (services, input, ct) =>
                services.GetRequiredService<IGetWeatherHandler>()
                    .GetWeatherAsync(input, ct));
```

The generated map method becomes conceptually:

```csharp
public static IEndpointRouteBuilder MapWeatherService(
    this IEndpointRouteBuilder endpoints,
    WeatherServiceProtocols protocols = WeatherServiceProtocols.RestJson1)
{
    return endpoints.MapSmithyService(
        WeatherHttpDefinition.Instance,
        protocols);
}
```

The descriptor should reference the existing transport-neutral service
definition and operation schemas rather than duplicate them. HTTP route data
must remain in an HTTP-specific definition; it does not belong in the core
`IServiceDefinition` used by MCP and other adapters.

### Caution

This change is worthwhile only if the descriptor model is simpler than the
generated endpoints it replaces. Strongly typed minimal-API delegates, route
selection, streaming body flags, and dependency-injection resolution can make a
universal descriptor abstraction more complicated than explicit generated
code.

Before implementing this step, compare generated source size and runtime
complexity on unary, streaming, multi-protocol, and static-query services. Thin,
readable generated endpoint code is acceptable; an abstract endpoint framework
that merely moves the same complexity is not an improvement.

## 4. Clarify HTTP-Specific Names

Several abstractions have broad names despite operating exclusively on neutral
HTTP messages:

```csharp
IProtocol
IServiceProtocol
IClientOperationProtocol<TInput, TOutput>
IServerOperationProtocol<TInput, TOutput>
SmithyOperationBinding<TInput, TOutput>
SmithyClientRuntime
```

Their methods use `SmithyHttpRequest`, `SmithyHttpClientResponse`, or
`SmithyHttpServerResponse`, and they live in the HTTP request/response execution
stack. Broad names may encourage future transports to reuse an abstraction that
does not match their semantics.

Clearer names would be:

```csharp
IHttpProtocol
IHttpServiceProtocol
IHttpClientOperationProtocol<TInput, TOutput>
IHttpServerOperationProtocol<TInput, TOutput>
SmithyHttpOperationBinding<TInput, TOutput>
SmithyHttpClientRuntime
```

gRPC still fits: its native message framing is carried over HTTP/2 and is
represented by the same neutral HTTP exchange types.

This is a public and pervasive rename, so it should wait for a planned breaking
release. The near-term architectural rule is more important: do not place
broker messaging behind these HTTP-shaped interfaces merely because they are
currently named `IProtocol` and `SmithyClientRuntime`.

## 5. Keep the Binding Stages

The current protocol construction chain is:

```text
IProtocol
    -> IServiceProtocol
        -> IClientOperationProtocol<TInput, TOutput>
        -> IServerOperationProtocol<TInput, TOutput>
```

It adds interfaces, but each transition has a useful lifetime:

- the unbound protocol carries protocol configuration;
- the service protocol compiles and caches service-level information;
- the operation protocol compiles bindings, codecs, validation, and error
  behavior once per operation and call side.

Collapsing the chain into `Bind(service, operation)` would shorten the API while
making caching and client/server specialization less explicit. Keep the stages
unless measurement shows that the service-bound layer carries no meaningful
state across protocols.

The combined `IOperationProtocol<TInput, TOutput>` remains a useful
implementation convenience. Client and server runtimes should continue to
depend only on their respective halves.

## 6. Use One Generated Service Definition as the Metadata Root

The server already generates a transport-neutral `IServiceDefinition` and an
operation catalog for adapters such as MCP. ASP.NET Core separately generates
typed handler resolution and protocol fields.

New host integrations should build on the existing definition and operation
schemas rather than introduce another parallel service model. Where an adapter
requires extra facts, it should add a small adapter-specific descriptor that
references the common operation definition.

The intended dependency direction is:

```text
generated ServiceSchema and OperationSchema
                    |
                    v
       generated IServiceDefinition
                    |
          +---------+---------+
          |                   |
          v                   v
 HTTP endpoint metadata    MCP metadata
          |                   |
          v                   v
 ASP.NET Core adapter      MCP adapter
```

No host-specific route, JSON Schema, or framework type should move into the
transport-neutral service definition merely to reduce generated lines.

## Priorities

Recommended implementation order:

1. inject `SmithyServerRuntime` instead of using static host state (implemented);
2. centralize generated client construction and transport ownership (implemented);
3. measure whether descriptor-driven endpoint mapping reduces total complexity;
4. consolidate new adapter metadata around the existing service definition;
5. reserve HTTP-specific naming changes for a breaking release.

The first two changes have a clear ownership benefit without changing the
operation or handler programming model. Endpoint descriptors and renames have a
higher migration cost and should be justified independently.

## Implementation Decisions

- The environment exposes `Create` for endpoint/HttpClient construction and
  `FromRuntime` for a caller-owned runtime. Both expose the service-bound
  protocol. Generated constructors dispose the environment if operation binding
  fails. Caller-owned resources survive both failure and client disposal.
- `ConfigureHttpClient` shares version selection with generated DI registration.
  Modeled preferences apply to the default protocol; an explicit protocol uses
  its own preference. Application factory configuration runs last.
- `AddSmithyServer` uses `TryAddSingleton`; aggregate handler registration calls
  it automatically. Individual handler registration requires an explicit call.
  Generated endpoints request the runtime with `[FromServices]`, preserving
  scoped application registrations and avoiding request-body inference.
- Direct callers of `SmithyAspNetCoreHost.DispatchAsync` now pass the runtime as
  the first argument. Generated client constructors and handler interfaces keep
  their existing signatures.
- Endpoint descriptors are deferred after examining unary, streaming,
  multi-protocol, and static-query generation. Each endpoint already delegates
  dispatch in one call. A descriptor would additionally need heterogeneous
  typed handler adapters, streaming flags, query guards, and protocol route
  collision handling. There is no demonstrated simplification to justify that
  extra public model in this change. Existing transport-neutral service and
  operation schemas remain the metadata root.
- HTTP-specific renames remain reserved for a separately planned breaking
  release. Protocol service/operation binding stages are retained.

## Success Criteria

- generated operation methods remain one-line runtime delegations;
- generated constructors contain service defaults and bindings, not a common
  transport-construction algorithm;
- server lifecycle configuration is available through dependency injection;
- adding a client or server lifecycle feature normally changes a runtime package
  rather than every generated service;
- protocol implementations remain independently testable from `HttpClient` and
  ASP.NET Core;
- generated code remains readable enough to diagnose a binding problem;
- no abstraction introduced merely to reduce line count hides HTTP, gRPC, or
  streaming semantics.

## Non-goals

- Unifying request/response execution and durable broker consumption behind one
  runtime interface.
- Removing generated typed clients or handler interfaces.
- Moving protocol wire rules into transports or host adapters.
- Making generated code minimal at the expense of opaque reflection-based
  dispatch.
- Collapsing service- and operation-binding stages without evidence that their
  caching boundary is unnecessary.

