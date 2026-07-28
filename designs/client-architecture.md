# Client Architecture

Target architecture for generated NSmithy clients and the shared client runtime.

## Goal

Generated clients should expose a natural .NET surface while delegating wire
behavior to protocol implementations and cross-cutting behavior to a shared
client lifecycle. The generated method should describe the modeled operation;
the runtime should own execution.

The desired shape is:

```text
start execution
  -> create execution context
  -> resolve endpoint and select auth scheme
  -> run before-execution interceptors
  -> prepare typed input
  -> serialize request
  -> resolve auth identity and signer
  -> sign request
  -> transmit attempt
  -> deserialize response or modeled error
  -> run completion interceptors
  -> return typed output
```

Endpoint resolution and auth scheme selection run before the before-execution
interceptors so every hook observes the effective endpoint in the context.

Retries wrap the attempt portion of the lifecycle. Telemetry observes both the
overall execution and individual attempts.

## Generated Client Surface

Each generated service has one interface and one concrete client:

```csharp
public interface IWeatherClient : IDisposable
{
    Task<GetForecastOutput> GetForecastAsync(
        GetForecastInput input,
        CancellationToken cancellationToken = default);
}
```

The concrete client has a per-service config type:

```csharp
public sealed class WeatherClientConfig : SmithyClientConfig
{
}
```

The public constructors keep transport ownership explicit:

```csharp
new WeatherClient(endpoint, config);      // client owns HttpClient
new WeatherClient(httpClient, config);    // caller owns HttpClient
new WeatherClient(runtime, config);       // caller owns full execution pipeline
```

`Endpoint` lives on config internally. The endpoint convenience constructor is
the common path: it copies the supplied config (shallow — strategy, interceptor,
and auth-scheme instances are shared by reference), sets the endpoint on the
copy, and delegates to the internal config-based construction path. Construction
never mutates a caller's config, so one config instance can safely parameterize
several clients. For static endpoints, the effective endpoint is resolved during
construction and placed into each invocation's `SmithyContext`.

Generated clients implement `IDisposable`. Disposal releases only resources the
client created itself.

## Configuration

`SmithyClientConfig` is the stable home for client knobs:

```csharp
public class SmithyClientConfig
{
    public Uri? Endpoint { get; set; }
    public IProtocol? Protocol { get; set; }
    public IEndpointResolver? EndpointResolver { get; set; }
    public IList<IClientInterceptor> Interceptors { get; }
    public IList<ISmithyAuthScheme> AuthSchemes { get; }
    public ISmithyRetryStrategy? RetryStrategy { get; set; }
    public TimeSpan? OperationTimeout { get; set; }
    public Func<string>? IdempotencyTokenProvider { get; set; }
}
```

Adding a property is version-friendly; adding constructor parameters is not. The
config object also maps cleanly to `IHttpClientFactory` and DI callback patterns.

## Execution Context

Every operation invocation gets a typed context:

```csharp
public sealed class ContextKey<T>(string name);

public sealed class SmithyContext
{
    public T? Get<T>(ContextKey<T> key);
    public void Set<T>(ContextKey<T> key, T value);
}
```

The context carries operation metadata, endpoint resolution state, selected auth
scheme, retry attempt state, telemetry objects, deadlines, and user-defined
values. It avoids untyped string bags while still allowing protocol and runtime
features to compose without adding parameters to every method.

## Interceptors

Interceptors are the primary extension point. They observe and modify named
lifecycle stages:

```csharp
public interface IClientInterceptor
{
    void OnBeforeExecution(SmithyContext context) { }
    void OnBeforeSerialization(SmithyContext context, object? input) { }

    ValueTask<SmithyHttpRequest> OnBeforeSigningAsync(
        SmithyContext context,
        SmithyHttpRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<SmithyHttpRequest> OnBeforeTransmitAsync(
        SmithyContext context,
        SmithyHttpRequest request,
        CancellationToken cancellationToken = default);

    void OnAfterTransmit(SmithyContext context, SmithyHttpClientResponse response) { }
    void OnAfterDeserialization(SmithyContext context, object? output) { }
    void OnAfterExecution(SmithyContext context, Exception? exception) { }
}
```

Every hook has a default implementation, so an interceptor overrides only the
stages it cares about. The signing and transmit hooks are async so they can do
I/O — fetching credentials or tokens — before the request goes out. Auth signing
is itself modeled as an interceptor.

`OnAfterExecution` runs on both success and failure; on failure it receives the
exception that will propagate to the caller (a modeled error, a client
exception, or a transport failure), so logging and metrics interceptors can
observe outcomes. The signing, transmit, and after-transmit hooks run once per
attempt — each retry is re-signed — while the remaining hooks run once per
execution.

Interceptors run in configured order before transmit and reverse order after
transmit/completion. They are scoped to the client and must be safe for
concurrent calls unless explicitly documented otherwise.

## Endpoint Resolution

Endpoint resolution can be per operation. The resolver sees the operation's
Smithy identifiers, the statically configured endpoint, and the typed input;
it is async so discovery-style resolvers can do I/O:

```csharp
public interface IEndpointResolver
{
    ValueTask<SmithyEndpoint> ResolveEndpointAsync(
        SmithyEndpointParameters parameters,
        CancellationToken cancellationToken = default);
}

public sealed record SmithyEndpoint(
    Uri Uri,
    IReadOnlyDictionary<string, string>? Headers = null,
    IReadOnlyList<string>? AuthSchemes = null);

public sealed record SmithyEndpointParameters(
    ShapeId ServiceId,
    ShapeId OperationId,
    Uri? ConfiguredEndpoint,
    object? Input);
```

A static `Config.Endpoint` is the simplest resolver (`StaticEndpointResolver`)
and is used when no resolver is configured; `Config.EndpointResolver` overrides
it for request routing. Resolution runs once per invocation, before
serialization. Resolved endpoint headers are added to every request sent to
that endpoint. Protocols and generated code apply host labels and operation
endpoint traits through the same resolution path when an endpoint depends on
operation metadata or input.

Resolved endpoints may narrow auth schemes: when `SmithyEndpoint.AuthSchemes`
is non-null, only modeled schemes also present there are considered by auth
selection. That lets endpoint rules and auth selection compose without
protocol-specific branches in generated clients.

## Auth

Auth has three separable concepts:

- **scheme resolution** chooses the effective modeled auth scheme for the
  operation.
- **identity resolution** obtains credentials or tokens for that scheme.
- **signing** mutates the serialized request before transmit.

Auth schemes are keyed by Smithy auth trait shape id. Per-operation `@auth`
overrides, endpoint-driven auth overrides, and anonymous operations all feed the
same resolver: each operation binding carries the operation's effective modeled
scheme ids (the service default, overridden by `@auth`), the resolved endpoint
may narrow that list, and the runtime selects the first scheme with a
configured interceptor per invocation. Configured schemes create their
interceptors once at client construction; selection is per call. The selected
auth interceptor runs after user interceptors in each request phase, so signing
sees the final request.

Identity providers own caching and refresh. Signers are stateless or explicitly
thread-safe services that operate on a request plus context.

## Retries

Retries are part of the client lifecycle, not a transport wrapper. A retry
strategy decides whether to retry after modeled errors, response metadata, or
transport failures. The runtime classifies each failed attempt first —
deserializing the modeled error when there is one — and then asks the strategy
for a decision that carries the backoff delay:

```csharp
public interface ISmithyRetryStrategy
{
    ISmithyRetrySession Begin();
}

public interface ISmithyRetrySession
{
    SmithyRetryDecision Classify(SmithyRetryOutcome outcome);
    void RecordSuccess();
}
```

A strategy is long-lived and shared by every execution of its client; it owns
client-wide state such as the retry quota and must be thread-safe. `Begin`
runs once per operation execution and returns that execution's session, which
owns per-execution state (for example, how much quota this execution
acquired). `SmithyRetryOutcome` is a failed attempt: the response (null on
transport failure) plus the exception that will propagate if the attempt is
not retried. The session owns the whole decision — attempt budgeting, failure
classification, and delay — and `RecordSuccess` lets quota-based strategies
refund what the execution's retries consumed. Stateless strategies return
themselves as the session.

The standard strategy uses:

- exponential backoff with full jitter
- a maximum backoff cap
- retry quota shared by the client
- `Retry-After` when present
- retryability from Smithy traits and protocol error classification
- `TimeProvider` for deterministic tests

Request streams are retried only when they can be replayed or when the caller
provides a replay strategy.

## Protocol Boundary

Protocols own wire behavior:

- request serialization
- response and error deserialization
- protocol-specific error discrimination
- event-stream framing
- payload binding
- trailer interpretation

Generated clients should not branch on protocol-specific wire rules. They bind
operation schemas once, select the configured protocol, and precompute
operation bindings that contain the service and operation shape ids and the
operation-bound protocol. The operation-bound protocol owns modeled error
deserialization and applies request-mutating traits (`@requestCompression`,
`@httpChecksumRequired`) during serialization — compression is a wire concern,
and its meaning differs by protocol (HTTP `Content-Encoding` vs gRPC
message-level compression). The operation method hot path then passes the
binding and typed input into the runtime.

## Observability

The runtime exposes OpenTelemetry-friendly primitives:

- `ActivitySource` spans for operation execution and retry attempts
- `Meter` counters/histograms for attempts, retries, latency, failures, and
  stream duration
- interceptor hooks for custom logging and diagnostics

Telemetry uses Smithy operation and service identifiers as stable names and
dimensions.

## Pagination

Operations with `@paginated` generate paginator helpers that return
`IAsyncEnumerable<T>`: `{Operation}PagesAsync` yields each response page while
the response carries a continuation token, and — when the trait names an
`items` list member — `{Operation}ItemsAsync` flattens the pages into their
items. Paginators use the normal client lifecycle for each page, so auth,
retries, endpoint resolution, and telemetry behave like any other operation
call.

## Streaming

Event streams and streaming blob payloads follow the streaming design. They are
not special cases outside the client lifecycle:

- event-stream operations use event-stream protocol bindings and
  `IAsyncEnumerable<TEvent>`
- streaming blobs use unary protocol bindings with a streaming HTTP body
  abstraction
- retries, auth, checksums, and telemetry flow through the same context and
  interceptor model

See [streaming.md](streaming.md) for the dedicated streaming architecture.
