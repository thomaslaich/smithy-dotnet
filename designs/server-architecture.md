# Server Architecture

Target architecture for generated NSmithy servers and the shared server runtime.

## Goal

Generated server code describes the modeled operations; the shared server
runtime and the protocols own routing and execution. A protocol says how
requests reach each operation; the runtime owns the dispatch algorithm:
deserialize the request, invoke the handler, serialize the response or a
modeled error. This mirrors [client-architecture.md](client-architecture.md),
where the runtime protocol already decides how a request is addressed: the
generated surface describes the operation, the runtime owns the lifecycle.

The desired shape is:

```text
receive request
  -> host adapter builds a neutral request
  -> server runtime dispatches:
       deserialize request
       invoke handler
       serialize response, or serialize a modeled error
  -> host adapter writes the neutral response
```

The dispatch algorithm and the routes live in hand-written runtimes, not in
codegen. A new protocol, a new host, or a new cross-cutting concern extends the
runtime, not a family of code-generation templates.

## The Seam

The server is the dual of the client stack:

```text
CLIENT                                   SERVER
  generated client                         generated endpoint (thin)
    -> SmithyClientRuntime                    -> SmithyServerRuntime      (hand-written, host-agnostic)
        -> IHttpTransport                     <- host adapter (host request/response <-> neutral)
        -> IClientOperationProtocol           -> IServerOperationProtocol
```

Two rules define the layering:

1. **The runtime is host-agnostic.** It takes a neutral request and returns a
   neutral response, exactly as `SmithyClientRuntime` takes an `IHttpTransport`
   and never touches `HttpClient`. The host adapter (e.g. the ASP.NET Core
   package) owns conversion between the host's request/response types and the
   neutral ones, and holds the only framework dependency. The runtime is
   unit-testable with a fake protocol and no host.
2. **The protocol owns trailer *content*; the host owns trailer *transport*.** A
   protocol decides that a response carries `grpc-status: 0`; the host decides
   whether the connection can carry trailers and how to write them. No wire
   detail of any protocol appears in the host adapter or generated code.

## Neutral Types

### Request

The server request is a `SmithyHttpRequest`. It carries method, request target,
headers, content type, and a `SmithyHttpBody` — which is `Streaming` (a raw body
stream) for event-stream operations. Streaming server protocol halves read the
body as a stream and deframe it; unary halves read the buffered bytes. One
request type serves every operation shape.

### Response — `SmithyHttpServerResponse`

Responses are directional: the server produces them and the host writes them.
One neutral type carries every response shape — unary bodies, streamed event
bodies, and protocol trailers:

```csharp
public sealed class SmithyHttpServerResponse
{
    public int StatusCode { get; init; } = 200;

    public IDictionary<string, IReadOnlyList<string>> Headers { get; }

    // Unary responses carry one chunk; streaming responses carry many.
    public IAsyncEnumerable<ReadOnlyMemory<byte>> Body { get; init; }

    // Set for unary responses so the host can emit Content-Length; null when streaming.
    public long? ContentLength { get; init; }

    // Trailer content, computed after the body completes. Receives the streaming error
    // (null = clean completion) so a protocol can map it — e.g. gRPC maps a mid-stream
    // failure to grpc-status:13. Null when the protocol has no trailers (REST / rpcv2Cbor).
    // The host decides whether the connection can carry trailers at all.
    public Func<Exception?, IReadOnlyList<KeyValuePair<string, string>>>? Trailers { get; init; }
}
```

The `Trailers` provider is what keeps gRPC's `grpc-status` / `grpc-message`
protocol-supplied data rather than host knowledge.

## Server Operation Protocol

The server half of an operation protocol deserializes the request and serializes
the response or a modeled error. Modeled errors resolve from the operation
schema the protocol already holds, so error identity and status are not passed in
from the caller:

```csharp
public interface IServerOperationProtocol<TInput, TOutput>
{
    // Validates deserialized input against the model's constraint traits before the
    // handler runs; null when the input carries no constraints. Not defaulted, so an
    // implementation that skips validation has to say so.
    ISmithyValidator<TInput>? InputValidator { get; }

    ValueTask<TInput> DeserializeRequestAsync(
        SmithyHttpRequest request,
        CancellationToken cancellationToken = default);

    SmithyHttpServerResponse SerializeResponse(
        TOutput output,
        CancellationToken cancellationToken = default);

    // Serializes a modeled error to a protocol error response. Returns false for an
    // unmodeled exception, which the runtime rethrows (surfaced as a 500 by the host).
    bool TrySerializeError(Exception exception, out SmithyHttpServerResponse response);
}
```

Event-stream server halves use the same interface. The operation input/output
shape carries any `IAsyncEnumerable<TEvent>` member; the protocol implementation
owns deframing request bodies and framing streamed response chunks.

## Server Runtime

`SmithyServerRuntime` is the dual of the client runtime's execution core:
hand-written, host-agnostic, and instance-based so server interceptors and
telemetry attach to it the way they attach to `SmithyClientRuntime`.

```csharp
public sealed class SmithyServerRuntime
{
    // Deserialize, invoke, serialize — with modeled errors caught once, for every protocol.
    public async Task<SmithyHttpServerResponse> DispatchAsync<TInput, TOutput>(
        IServerOperationProtocol<TInput, TOutput> protocol,
        SmithyHttpRequest request,
        Func<TInput, CancellationToken, Task<TOutput>> handler,
        CancellationToken cancellationToken)
    {
        var input = await protocol.DeserializeRequestAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (protocol.InputValidator is { } validator)
        {
            var errors = validator.GetErrors(input);
            // Answered with smithy.framework#ValidationException.
        }

        try
        {
            var output = await handler(input, cancellationToken).ConfigureAwait(false);
            return protocol.SerializeResponse(output, cancellationToken);
        }
        catch (Exception ex) when (protocol.TrySerializeError(ex, out var errorResponse))
        {
            return errorResponse;
        }
    }
}
```

One dispatch algorithm covers every protocol and operation shape. Modeled-error
handling is a single `catch` filtered by `TrySerializeError`, not a generated
`catch` block per error.

## Input Validation

Constraint traits are enforced on the server, between deserialization and the
handler, so a handler only ever sees input the model permits. The client does not
validate: it is not the authority on the contract, and duplicating the check
there costs latency without buying safety.

`SmithyValidator.FromSchema` compiles an operation's input schema into a tree of
validators once, when the protocol is built — the same compile-once shape the
codecs use. It returns `null` when nothing reachable from the schema carries a
constraint, and the runtime then skips validation altogether, so an unconstrained
operation pays nothing. Constraints are read from the shape's kind rather than
its CLR type, because an optional member wraps its target in a nullable schema
and a member schema is its own kind; a constraint the kind says should apply but
that cannot be enforced fails at compile time rather than being dropped. Enum
membership comes from the values the schema carries, and `@uniqueItems` compares
elements through equality derived from the schema rather than from .NET, which
would compare a blob or a structure holding a collection by reference.

A violation becomes `smithy.framework#ValidationException`, which every operation
carries as an implicit modeled error so protocols serialize it like any other and
generated clients can deserialize it. Each violation is reported as a
`ValidationExceptionField` whose `path` is a JSONPointer into the input.

A `@required` member absent from the payload fails inside the codec, before the
compiled validator runs, because the builder cannot produce the shape without it.
Codecs signal that with `MissingRequiredMemberException`, which the runtime
converts into the same `ValidationException` — a caller's omission is a 400 on
the server and an exception on the client, from one throw site. The reader that
finds the omission knows only the member's own name, so each enclosing reader
prepends its own step as the exception unwinds and the member is reported where
it actually sits.

## Host Adapter

A host adapter binds a host framework to the runtime. It owns two conversions —
the host request into a `SmithyHttpRequest`, and a `SmithyHttpServerResponse` onto
the host response — the mapping of protocols' routes to host endpoints, and one
dispatch entry point:

The host adapter resolves `SmithyServerRuntime` from request services, and uses
a default runtime when the application registers none. `AddSmithyServer()`,
which `Add{Service}Handler<THandler>()` calls, registers the runtime with
`TryAddSingleton`, so an application registration takes precedence. The runtime
is currently stateless; resolving it from the container allows future lifecycle
configuration without adding global host state.

```csharp
public static class SmithyAspNetCoreHost
{
    public static async Task DispatchAsync<TInput, TOutput>(
        SmithyServerRuntime runtime,
        HttpContext context,
        IServerOperationProtocol<TInput, TOutput> protocol,
        Func<TInput, CancellationToken, Task<TOutput>> handler,
        bool streamRequestBody = false,
        CancellationToken cancellationToken = default)
    {
        var request = await ToSmithyRequestAsync(context, streamRequestBody, cancellationToken).ConfigureAwait(false);
        var response = await runtime.DispatchAsync(protocol, request, handler, cancellationToken).ConfigureAwait(false);
        await WriteAsync(context, response, cancellationToken).ConfigureAwait(false);
    }

    // One writer for every protocol. Trailer values come from the response; the host only
    // decides whether the connection can carry trailers and writes whatever the response holds.
    private static async Task WriteAsync(HttpContext context, SmithyHttpServerResponse response, CancellationToken ct)
    {
        context.Response.StatusCode = response.StatusCode;
        foreach (var header in response.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }
        if (response.ContentLength is { } length)
        {
            context.Response.ContentLength = length;
        }

        var supportsTrailers = response.Trailers is not null && context.Response.SupportsTrailers();
        await context.Response.StartAsync(ct).ConfigureAwait(false);

        Exception? streamError = null;
        try
        {
            await foreach (var chunk in response.Body.WithCancellation(ct).ConfigureAwait(false))
            {
                await context.Response.Body.WriteAsync(chunk, ct).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (supportsTrailers)
        {
            streamError = ex;
        }
        // When the connection cannot carry trailers, a mid-stream failure propagates and aborts
        // the response so the peer sees a broken stream, not a clean completion.

        if (response.Trailers is { } trailers)
        {
            foreach (var trailer in trailers(streamError))
            {
                context.Response.AppendTrailer(trailer.Key, trailer.Value);
            }
        }

        await context.Response.CompleteAsync().ConfigureAwait(false);
    }
}
```

Whether a connection can carry trailers is transport-level and belongs to the
host. The trailer values (`grpc-status: 13`, the message) belong to the protocol.

Direct callers of `SmithyAspNetCoreHost.DispatchAsync` pass the runtime as the
first argument; mapped endpoints resolve it from request services, preserving
application-selected lifetimes. This is an ownership seam; server interceptors
and telemetry remain future work.

## Routing

Routing belongs to the protocol. A protocol already decides how a client
addresses a request; on the server, the same knowledge decides which operation a
request is for. The server half of `IServiceProtocol` reports it:

```csharp
public interface IServiceProtocol
{
    IClientOperationProtocol<TInput, TOutput> ForClientOperation<TInput, TOutput>(
        OperationSchema<TInput, TOutput> operation);

    IServerOperationProtocol<TInput, TOutput> ForServerOperation<TInput, TOutput>(
        OperationSchema<TInput, TOutput> operation);

    // How requests reach the given operations over this protocol. A protocol that does not
    // serve requests keeps the default, ServerRouting.None.
    ServerRouting GetServerRouting(IReadOnlyList<IOperationSchema> operations) =>
        ServerRouting.None;
}

public abstract record ServerRouting
{
    public static ServerRouting None { get; } = new ByRoute([]);

    // Each operation has its own route, matched by method and path.
    public sealed record ByRoute(IReadOnlyList<ServerRoute> Routes) : ServerRouting;

    // One route for the service; the protocol names the operation from the request.
    public sealed record ByDispatch(
        string Method,
        string Path,
        Func<SmithyHttpRequest, CancellationToken, ValueTask<ShapeId?>> ResolveOperation,
        Func<SmithyHttpRequest, SmithyHttpServerResponse> UnknownOperation) : ServerRouting;
}

public sealed record ServerRoute(
    ShapeId Operation,
    string Method,
    string Path,                                             // {label} and {label+} segments
    IReadOnlyList<KeyValuePair<string, string?>> RequiredQuery);  // literal query of @http(uri)
```

| Protocol | Routing |
| --- | --- |
| restJson1, simpleRestJson | `ByRoute`: each operation's `@http` method and URI, read from its schema |
| rpcv2Cbor, rpcv2Json | `ByRoute`: `POST /service/{Service}/operation/{Operation}` |
| gRPC | `ByRoute`: `POST /{namespace}.{Service}/{Operation}` |
| awsJson1_0, awsJson1_1 | `ByDispatch`: `POST /`, the operation named by `X-Amz-Target` |

`ByRoute` covers protocols that address operations by method and path, so the
host matches requests with its own router. `ByDispatch` covers protocols that
name the operation inside the request (a header, or the body for JSON-RPC and
Thrift). The protocol may read the body to resolve the operation; the host
buffers it, so the operation protocol deserializes the same bytes. An operation
that streams its request body therefore cannot be routed by dispatch, and
mapping one fails at startup. When the
protocol names no operation, the host writes the protocol's `UnknownOperation`
response, so the error has the protocol's shape (a JSON-RPC "method not found",
say).

A protocol leaves out operations it cannot bind, such as event-stream
operations on a protocol without event streams. Paths are protocol-neutral URI
patterns; the host adapter translates them into its own routing syntax. A
request whose literal query does not match `RequiredQuery` is not routed to the
operation and is answered with 404, as an unmatched route would be.

`IServerOperationProtocol.StreamsRequestBody` tells the host whether to hand the
operation the request body as a stream (event-stream input, a streaming blob
payload) or buffered. Like `InputValidator`, it has no default, so a protocol
that streams has to say so.

## Generated Server Surface

Generated code describes operations; it contains no routes, no protocol
branches, and no per-operation endpoint. For each service it emits one operation
table, the `ServiceOperationCatalog` of its `{Service}Definition`. Each entry pairs
an operation's schema with a typed handler resolver and, for unary operations,
the JSON Schemas of its input and output:

```csharp
internal static ServiceOperationCatalog Catalog { get; } = new(
    WeatherSchema.Schema,
    ServerOperation.Create(
        GetCitySchema.Schema,
        services => services.GetRequiredService<IGetCityHandler>().GetCityAsync,
        GetCityJsonSchemas.Value),
    ServerOperation.Create(
        GetForecastSchema.Schema,
        services => services.GetRequiredService<IGetForecastHandler>().GetForecastAsync,
        GetForecastJsonSchemas.Value));
```

Every host adapter works from this table: HTTP routing binds its operations to
protocols, and MCP exposes them as tools. `IServiceDefinition.Operations`
returns it, so adapters find it through the registered definitions.

`ServerOperation<TInput, TOutput>` keeps the operation's types, so neither
adapter needs reflection. For HTTP, `Bind` creates the typed
`IServerOperationProtocol` from a protocol's `IServiceProtocol` once per mapped
protocol. For adapters that hold the input as an object, such as MCP after
decoding tool arguments, `InvokeAsync(input, services, cancellationToken)`
validates the input against the schema and calls the handler. Both resolve the
handler from the services of the request they serve, so handler lifetimes are
the application's in every adapter.

`Map{Service}` keeps its signature and maps the selected protocols through the
host adapter:

```csharp
public static IEndpointRouteBuilder MapWeatherService(
    this IEndpointRouteBuilder endpoints,
    WeatherServiceProtocols protocols = WeatherServiceProtocols.RestJson1)
{
    var selected = new List<SmithyServerProtocol>();
    if ((protocols & WeatherServiceProtocols.RestJson1) != 0)
    {
        selected.Add(new("RestJson1", new RestJson1Protocol()));
    }

    return endpoints.MapSmithyService(WeatherServiceDefinition.Catalog, selected);
}
```

`MapSmithyService` is public: it maps any service from its operation catalog and
a list of `SmithyServerProtocol(Name, Protocol)` values, the name appearing in
endpoint display names and route conflict errors.

The host adapter turns each protocol's `ServerRouting` into endpoints: one per
`ServerRoute`, named `{Operation} ({Protocol})`, or one for a `ByDispatch`
protocol. Endpoints are plain `RequestDelegate`s, so mapping uses no minimal-API
parameter binding and needs neither reflection nor the request delegate
generator. A route that repeats the method and path of an earlier one within
one `MapSmithyService` call fails at startup, naming the protocol that repeats
it.

New hosts reuse the operation catalog, the service and operation schemas, and
the protocols' `ServerRouting`. HTTP route syntax and framework types belong in
their adapters, not in a parallel service model.

## Multiple Protocols per Service

A service can be exposed over several protocols at once — restJson1, rpcv2Cbor,
gRPC — from **one set of handlers**. This is a consequence of the layering, not a
bolted-on feature:

- The handler interface is protocol-agnostic. `IFooHandler.FooAsync(FooInput, ct)`
  deals only in typed model values; there is one handler interface per service,
  derived from the operation shapes.
- All protocol knowledge lives in the operation-protocol binding.
  `new RestJson1Protocol().ForService(schema).ForServerOperation(op)` and
  `new RpcV2CborProtocol().ForService(schema).ForServerOperation(op)` are two different
  `IServerOperationProtocol<FooInput, FooOutput>` over the same typed contract.
- Dispatch is parameterized by that binding, so the same handler delegate serves
  every protocol.

Codegen emits one `Map{Service}` endpoint extension with a generated
`{Service}Protocols` flags enum. Each selected protocol builds its own bindings
and resolves the same DI-registered handler:

```csharp
app.MapWeatherService(
    WeatherServiceProtocols.RestJson1 | WeatherServiceProtocols.RpcV2Cbor);
services.AddWeatherServiceHandler<MyWeatherHandler>();   // one handler, both
```

Two rules keep the handler shared across protocols:

- The handler interface's streaming surface derives from the model (`@streaming`),
  not from any one protocol. Event framing differs per protocol (vnd.amazon.eventstream
  versus gRPC's length prefix), but that lives entirely inside the binding; the
  handler surface `IAsyncEnumerable<TEvent>` is identical across protocols.
- Modeled errors and their status codes come from the schema, so each protocol
  binding serializes the same thrown exception its own way.

### Listeners and Ports

Generated `Map{Service}` extensions are port-agnostic — ports are a deployment
concern, not a model concern, so codegen never binds one. Whether two
protocols can share a listener depends on their routes and transport:

- **Disjoint routes share a port.** restJson1 (`@http` paths), rpcv2Cbor
  (`/service/…/operation/…`), and the awsJson family (`POST /` with `X-Amz-Target`)
  occupy different route shapes, so any mix of these three coexists on one
  listener; a client selects its protocol by which route it calls.
- **Same-route protocols need separate listeners.** awsJson1_0 and awsJson1_1
  both bind `POST /` and differ only by `Content-Type`. Routing does not dispatch
  on `Content-Type`, so exposing both requires pinning each to its own port with
  `RequireHost`.
- **Transport-incompatible protocols need separate listeners.** gRPC requires
  HTTP/2; it goes on its own HTTP/2 listener rather than sharing an HTTP/1.1 port.

The scoping mechanism is ASP.NET Core's `RequireHost`, applied by the app, not the
generator:

```csharp
app.MapGroup("")
    .RequireHost("*:5002")
    .MapWeatherService(WeatherServiceProtocols.Grpc);   // gRPC on its own HTTP/2 port
```

## Streaming Type Naming

The request and response body types are shared with the client and named for
what actually streams; see
[streaming.md](streaming.md#framing-and-the-streaming-transport).

## Non-goals

- **Client-side validation of outbound input.** The client is not the authority
  on the contract — the server is — so checking there would duplicate the check
  that actually decides, cost latency on every call, and drift the moment a
  model changes without a client rebuild. A generated client sends what it is
  given and surfaces the server's `ValidationException` as a modeled error.
- Protocol-specific types (gRPC status codes, trailer names, media types) do not
  appear in the host adapter or generated server code.
- Codegen does not own the dispatch algorithm or the routes; the runtime and the
  protocols are their single owners.
- Server interceptors and telemetry are out of scope for the initial runtime; the
  runtime only provides the seam where they attach later, as on the client.

## Related Docs

- [client-architecture.md](client-architecture.md) — the client stack this mirrors
- [streaming.md](streaming.md) — event-stream protocol interfaces and framing
- [http-interfaces.md](http-interfaces.md) — transport abstractions
- [codegen-architecture.md](codegen-architecture.md) — codegen pipeline
