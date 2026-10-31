# Server Architecture

How a generated NSmithy server turns an HTTP request into a handler call, and
which part of the system owns each step.

## Overview

A generated server is mostly description. Codegen emits the model's types, the
handler interfaces, and a table of the service's operations. Everything that
acts on a request is hand-written runtime code, split by responsibility:

| Part | Owns | Knows nothing about |
| --- | --- | --- |
| Generated code | the operation table, handler interfaces, `Map{Service}` | routes, wire formats, the host |
| Protocol | routes, deserializing requests, serializing responses and errors | the host framework, handlers |
| Server runtime | the dispatch algorithm: validate, call the handler, catch modeled errors | wire formats, the host |
| Host adapter | turning routes into endpoints, converting requests and responses | wire formats, the model |
| Handler | the application's logic, in typed model values | protocols, HTTP |

Because each concern has one owner, a new protocol, a new host, or a
cross-cutting concern such as telemetry is added in one place rather than in
generated code. The split mirrors
[client-architecture.md](client-architecture.md), where generated code also
describes operations and the runtime and protocols own the request lifecycle.

The rest of this document follows one example service:

```smithy
@restJson1
service WeatherService {
    operations: [GetCity]
}

@readonly
@http(method: "GET", uri: "/cities/{cityId}")
operation GetCity {
    input := {
        @required @httpLabel @length(min: 1)
        cityId: String
    }
    output := { name: String }
}
```

## How a Request Is Served

Serving happens in two phases.

**At startup**, the application registers its handler and maps the service:

```csharp
builder.Services.AddWeatherServiceHandler<WeatherHandler>();
app.MapWeatherService(WeatherServiceProtocols.RestJson1);
```

`MapWeatherService` hands the generated operation table and the selected
protocols to the host adapter. For each protocol, the adapter binds every
operation to it, asks the protocol for its routes, and registers one endpoint
per route. restJson1 reads `GetCity`'s `@http` trait and answers
`GET /cities/{cityId}`.

**Per request**, a call to `GET /cities/zurich` flows through that endpoint:

```text
host adapter     converts the HttpContext into a SmithyHttpRequest
  -> binding     GetCity bound to restJson1; resolves the handler from the request's services
  -> runtime     protocol deserializes the input
                 validator checks the constraints (@length on cityId)
                 handler runs: GetCityAsync(input, ct)
                 protocol serializes the output, or a modeled error
host adapter     writes the SmithyHttpServerResponse to the HttpContext
```

The sections below take each part in the order a request meets it.

## The Generated Operation Table

For each service, codegen emits one operation table: the
`ServiceOperationCatalog` of its `{Service}Definition`. Each entry pairs an
operation's schema with a function that resolves its handler from a request's
services, plus the JSON Schemas of its input and output for adapters such as
MCP:

```csharp
internal static ServiceOperationCatalog Catalog { get; } = new(
    WeatherServiceSchema.Schema,
    ServerOperation.Create(
        GetCitySchema.Schema,
        services => services.GetRequiredService<IGetCityHandler>().GetCityAsync,
        GetCityJsonSchemas.Value));
```

The table contains no routes and no protocol code. `Map{Service}` only
translates the generated `{Service}Protocols` flags into protocol instances:

```csharp
public static SmithyServiceEndpointConventionBuilder MapWeatherService(
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

`ServerOperation<TInput, TOutput>` keeps the operation's input and output
types, so no adapter needs reflection. It offers two ways in:

- `Bind(protocol)` pairs the operation with one protocol, producing an
  `IServerOperationBinding` that dispatches wire requests. HTTP adapters use
  this.
- `InvokeAsync(runtime, input, services, ct)` takes input that is already
  decoded. Adapters without a wire request use this, such as MCP after
  decoding tool arguments.

Both resolve the handler from the services of the request being served, so
handler lifetimes are the application's choice. Both run through the server
runtime, so validation and anything attached to the runtime apply to every
adapter. `IServiceDefinition.Operations` exposes the table, so an adapter can
find it from the registered definitions.

## Protocols

### Three levels

A protocol is used at three levels, each knowing more than the one before:

```text
IProtocol                          the protocol itself: new RestJson1Protocol()
  .ForService(serviceSchema)
-> IServiceProtocol                applied to one service; sees every operation
  .ForServerOperation(operation)
-> IServerOperationProtocol<I, O>  applied to one operation; typed
```

A question belongs to the level that has the information to answer it.
Routing is a service-level question: a protocol that names operations in a
header answers it with one route for the whole service, and route conflicts
only show across all operations. Deserializing a request is an operation-level
question and needs the operation's types. The client uses the same levels, with
`ForClientOperation` at the bottom.

### Routing

The service-level protocol reports how requests reach operations:

```csharp
public interface IServiceProtocol
{
    // ForClientOperation and ForServerOperation omitted.

    // A protocol that does not serve requests keeps the default.
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
    string Path,                                                  // {label} and {label+} segments
    IReadOnlyList<KeyValuePair<string, string?>> RequiredQuery);  // literal query of @http(uri)
```

| Protocol | Routing |
| --- | --- |
| restJson1, simpleRestJson | `ByRoute`: each operation's `@http` method and URI |
| rpcv2Cbor, rpcv2Json | `ByRoute`: `POST /service/{Service}/operation/{Operation}` |
| gRPC | `ByRoute`: `POST /{namespace}.{Service}/{Operation}` |
| awsJson1_0, awsJson1_1 | `ByDispatch`: `POST /`, the operation named by `X-Amz-Target` |

`ByRoute` lets the host's own router match requests. `ByDispatch` covers
protocols that name the operation inside the request, in a header or in the
body (JSON-RPC, Thrift). To find the operation, the host buffers the body and
the protocol reads it; the operation then deserializes the same bytes. An
operation that streams its request body therefore cannot be routed by
dispatch, and mapping one fails at startup. When the protocol names no
operation, the host writes the protocol's `UnknownOperation` response, so the
error has the protocol's shape (a JSON-RPC "method not found", say).

A protocol leaves out operations it cannot serve, such as event-stream
operations on a protocol without event streams. Paths use Smithy's URI syntax;
the host adapter translates them into its own route templates. A request whose
query lacks the route's `RequiredQuery` literals is answered with 404, like an
unmatched route.

### The operation protocol

The operation-level protocol handles one operation's requests and responses:

```csharp
public interface IServerOperationProtocol<TInput, TOutput>
{
    // Checks deserialized input against the constraint traits; null when there are none.
    ISmithyValidator<TInput>? InputValidator { get; }

    // Whether the body reaches DeserializeRequestAsync as a stream rather than buffered.
    bool StreamsRequestBody { get; }

    ValueTask<TInput> DeserializeRequestAsync(
        SmithyHttpRequest request,
        CancellationToken cancellationToken = default);

    SmithyHttpServerResponse SerializeResponse(
        TOutput output,
        CancellationToken cancellationToken = default);

    // False for an unmodeled exception, which the runtime rethrows.
    bool TrySerializeError(Exception exception, out SmithyHttpServerResponse response);
}
```

`StreamsRequestBody` is true for an event-stream input and, on REST protocols,
for a streaming blob payload; the host needs the answer before it reads the
body. Neither `InputValidator` nor `StreamsRequestBody` has a default, so a
protocol that skips validation or streams has to say so rather than get it
wrong silently.

Modeled errors resolve from the operation schema the protocol holds, so the
runtime passes only the exception. Event-stream operations use the same
interface: the input or output carries an `IAsyncEnumerable<TEvent>` member,
and the protocol frames and deframes the events ([streaming.md](streaming.md)).

## The Host Adapter

A host adapter connects a web framework to the rest of the system and holds
the only framework dependency. The ASP.NET Core adapter does five things.

**Maps endpoints.** `MapSmithyService(catalog, protocols)` creates one endpoint
per `ServerRoute`, named `{Operation} ({Protocol})`, or one per `ByDispatch`
protocol. It translates greedy labels (`/files/{key+}`) into ASP.NET Core
catch-alls (`/files/{**key}`). Endpoints are plain `RequestDelegate`s, so
mapping needs no minimal-API parameter binding, no reflection, and no request
delegate generator. A route that repeats the method and path of an earlier one
fails at startup, naming the protocol that repeats it. Each endpoint carries
`SmithyEndpointMetadata` naming the service, the protocol, and the operations
it serves, and `MapSmithyService` returns a convention builder over all of the
service's endpoints, so ASP.NET Core conventions apply per service and can
tell operations apart.

**Authorizes from the model.** Each catalog entry carries its operation's
effective auth schemes, which codegen resolves with Smithy's `ServiceIndex`,
including `smithy.api#noAuth` when the operation can be called without
authentication. `WithModeledAuthorization()` turns them into endpoint metadata:
`[Authorize]` for operations that require authentication, `[AllowAnonymous]`
for the rest. A `ByDispatch` endpoint whose operations differ allows anonymous
calls and authorizes a protected operation after resolving it, through the same
policy evaluator and result handler as ASP.NET Core's authorization middleware.
Checking credentials stays with the application's authentication handlers.

**Converts the request.** The `HttpContext` becomes a `SmithyHttpRequest`
carrying the method, target, headers and a `SmithyHttpBody`: buffered bytes,
or the live request stream when the operation's `StreamsRequestBody` is set.
For a streamed body the host also relaxes Kestrel's minimum data rate, since a
long-lived stream can legitimately go quiet.

**Resolves the runtime.** Each request uses the `SmithyServerRuntime`
registered in the request's services, or `SmithyServerRuntime.Default` when
there is none. `Add{Service}Handler<THandler>()` registers one with
`TryAddSingleton`, so an application's own registration takes precedence.

**Writes the response.** One neutral type carries every response shape:

```csharp
public sealed class SmithyHttpServerResponse
{
    public int StatusCode { get; init; } = 200;
    public IDictionary<string, IReadOnlyList<string>> Headers { get; }

    // One chunk for a unary response, many for a streamed one.
    public IAsyncEnumerable<ReadOnlyMemory<byte>> Body { get; init; }

    // Set for unary responses; null when streaming.
    public long? ContentLength { get; init; }

    // Computed after the body completes, given the streaming error (null on success).
    // Null when the protocol has no trailers.
    public Func<Exception?, IReadOnlyList<KeyValuePair<string, string>>>? Trailers { get; init; }
}
```

The host writes the status, headers and body chunks, then the trailers. The
trailer *content* belongs to the protocol: gRPC maps a mid-stream failure to
`grpc-status: 13`. The trailer *transport* belongs to the host: whether the
connection can carry trailers at all. When it cannot, a mid-stream failure
aborts the response, so the peer sees a broken stream rather than a clean
completion. No protocol's wire details appear in the host adapter.

## The Server Runtime

`SmithyServerRuntime` owns the dispatch algorithm. It takes neutral requests
and returns neutral responses, so it is unit-testable with a fake protocol and
no host:

```csharp
public sealed class SmithyServerRuntime
{
    public static SmithyServerRuntime Default { get; } = new();

    // For a wire request: deserialize, validate, invoke, serialize.
    public Task<SmithyHttpServerResponse> DispatchAsync<TInput, TOutput>(
        IServerOperationProtocol<TInput, TOutput> protocol,
        SmithyHttpRequest request,
        Func<TInput, CancellationToken, Task<TOutput>> handler,
        CancellationToken cancellationToken);

    // For decoded input: validate, invoke. A violation throws ValidationException.
    public Task<TOutput> InvokeAsync<TInput, TOutput>(
        ISmithyValidator<TInput>? validator,
        TInput input,
        Func<TInput, CancellationToken, Task<TOutput>> handler,
        CancellationToken cancellationToken);
}
```

`DispatchAsync` is one algorithm for every protocol and operation shape:

```csharp
var input = await protocol.DeserializeRequestAsync(request, ct);
// A constraint violation is answered with smithy.framework#ValidationException.
var output = await handler(input, ct);
return protocol.SerializeResponse(output, ct);
// A thrown exception is serialized when protocol.TrySerializeError accepts it and
// rethrown otherwise, which the host answers with 500.
```

Both entry points call the handler through the same step, which is where
server interceptors and telemetry attach, as they do on `SmithyClientRuntime`.
That is why the runtime is an instance resolved from the container rather than
a static class.

### Input validation

Constraint traits are enforced between deserialization and the handler, so a
handler only sees input the model permits. The server enforces them because it
is the authority on the contract.

`SmithyValidator.FromSchema` compiles an operation's input schema into
validators once, when the protocol is built, like the codecs compile their
serializers. It returns `null` when no constraint is reachable from the
schema, so an unconstrained operation pays nothing.

A violation becomes `smithy.framework#ValidationException`. Every operation
carries it as an implicit modeled error, so each protocol serializes it like
any other error and generated clients deserialize it. Each violation is a
`ValidationExceptionField` whose `path` is a JSON Pointer into the input.

A missing `@required` member is caught earlier, in the codec, which cannot
build the shape without it. The codec throws `MissingRequiredMemberException`,
which the runtime converts into the same `ValidationException`. The path is
complete because each enclosing reader prepends its step as the exception
unwinds.

## Multiple Protocols per Service

One set of handlers can serve a service over several protocols at once:

```csharp
builder.Services.AddWeatherServiceHandler<WeatherHandler>();
app.MapWeatherService(WeatherServiceProtocols.RestJson1 | WeatherServiceProtocols.RpcV2Cbor);
```

This follows from the layering. The handler interface deals only in typed
model values, all wire knowledge lives in the operation protocol, and
`MapSmithyService` binds each operation once per protocol, with every binding
resolving the same handler. Streaming surfaces derive from the model
(`@streaming`), not from a protocol: event framing differs per protocol, but
the handler always sees `IAsyncEnumerable<TEvent>`. Modeled errors come from
the schema, so each protocol serializes the same thrown exception its own way.

### Listeners and ports

`Map{Service}` never binds a port: ports are a deployment concern. Whether
protocols can share a listener depends on their routes and transport:

- **Disjoint routes share a port.** restJson1 (`@http` paths), rpcv2Cbor
  (`/service/…/operation/…`) and awsJson (`POST /` with `X-Amz-Target`) use
  different route shapes, so a client selects a protocol by the route it calls.
- **Identical routes need separate listeners.** awsJson1_0 and awsJson1_1 both
  use `POST /` and differ only in `Content-Type`, which routing ignores.
  Mapping both in one `MapSmithyService` call fails at startup.
- **Different transports need separate listeners.** gRPC requires HTTP/2.

The application scopes protocols to a listener with ASP.NET Core's
`RequireHost`:

```csharp
app.MapGroup("")
    .RequireHost("*:5002")
    .MapWeatherService(WeatherServiceProtocols.Grpc);   // gRPC on its own HTTP/2 port
```

## Extending

- **A new protocol** implements `IProtocol`. To serve requests it implements
  `ForServerOperation` and `GetServerRouting`; generated code and host
  adapters need no changes.
- **A new HTTP host** converts its request and response types to and from
  `SmithyHttpRequest` and `SmithyHttpServerResponse`, turns `ServerRouting`
  into its endpoints, and dispatches through `IServerOperationBinding`.
- **A non-HTTP adapter** works from the operation table directly. The MCP
  adapter describes each operation as a tool through its JSON Schemas, decodes
  tool arguments with its own JSON codec, calls `InvokeAsync`, and reports
  modeled errors as tool errors.

## Non-goals

- **Client-side validation.** The server decides; a client check would add
  latency to every call and drift from the server whenever the model changes
  without a client rebuild. A generated client surfaces the server's
  `ValidationException` as a modeled error.
- **Transports other than HTTP.** Other transports reuse the generated types
  and codecs through their own integrations rather than the protocol stack.

## Related Docs

- [client-architecture.md](client-architecture.md): the client stack this mirrors
- [streaming.md](streaming.md): event-stream protocol interfaces and framing
- [http-interfaces.md](http-interfaces.md): transport abstractions
- [codegen-architecture.md](codegen-architecture.md): the codegen pipeline
