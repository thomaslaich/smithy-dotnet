# Server Architecture

Target architecture for generated NSmithy servers and the shared server runtime.

## Goal

Generated server code describes the modeled operation and its routing; a shared
server runtime owns execution. The generated endpoint maps a route to a handler
method and the operation's bound protocol; the runtime owns the dispatch
algorithm — deserialize the request, invoke the handler, serialize the response
or a modeled error. This mirrors [client-architecture.md](client-architecture.md):
the generated surface describes the operation, the runtime owns the lifecycle.

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

The dispatch algorithm lives in one hand-written runtime, not in codegen. A new
protocol, a new host, or a new cross-cutting concern extends the runtime, not a
family of code-generation templates.

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
    TInput DeserializeRequest(SmithyHttpRequest request);

    SmithyHttpServerResponse SerializeResponse(TOutput output);

    // Serializes a modeled error to a protocol error response. Returns false for an
    // unmodeled exception, which the runtime rethrows (surfaced as a 500 by the host).
    bool TrySerializeError(Exception exception, out SmithyHttpServerResponse response);
}
```

Streaming server halves return the same `SmithyHttpServerResponse`, building the
`Body` from framed chunks and attaching their `Trailers` provider:

```csharp
public interface IOutputEventStreamServerProtocol<TInput, TOutputEvent>
{
    TInput DeserializeRequest(SmithyHttpRequest request);
    SmithyHttpServerResponse SerializeResponse(
        IAsyncEnumerable<TOutputEvent> events,
        CancellationToken cancellationToken = default);
}

public interface IInputEventStreamServerProtocol<TInputEvent, TOutput>
{
    IAsyncEnumerable<TInputEvent> DeserializeRequestEventsAsync(
        SmithyHttpRequest request,
        CancellationToken cancellationToken = default);
    SmithyHttpServerResponse SerializeResponse(TOutput output);
}
```

## Server Runtime

`SmithyServerRuntime` is the dual of the client runtime's execution core:
hand-written, host-agnostic, and instance-based so server interceptors and
telemetry attach to it the way they attach to `SmithyClientRuntime`.

```csharp
public sealed class SmithyServerRuntime
{
    // Unary: deserialize, invoke, serialize — with modeled errors caught once, for every protocol.
    public async Task<SmithyHttpServerResponse> DispatchAsync<TInput, TOutput>(
        IServerOperationProtocol<TInput, TOutput> protocol,
        SmithyHttpRequest request,
        Func<TInput, CancellationToken, Task<TOutput>> handler,
        CancellationToken cancellationToken)
    {
        var input = protocol.DeserializeRequest(request);
        try
        {
            var output = await handler(input, cancellationToken).ConfigureAwait(false);
            return protocol.SerializeResponse(output);
        }
        catch (Exception ex) when (protocol.TrySerializeError(ex, out var errorResponse))
        {
            return errorResponse;
        }
    }

    // Output stream: unary in, events out. Body and Trailers both come from the protocol.
    public SmithyHttpServerResponse DispatchOutputStream<TInput, TOutputEvent>(
        IOutputEventStreamServerProtocol<TInput, TOutputEvent> protocol,
        SmithyHttpRequest request,
        Func<TInput, CancellationToken, IAsyncEnumerable<TOutputEvent>> handler,
        CancellationToken cancellationToken)
    {
        var input = protocol.DeserializeRequest(request);
        return protocol.SerializeResponse(handler(input, cancellationToken), cancellationToken);
    }

    // Input stream and duplex are the same shape over the other protocol halves.
}
```

One dispatch algorithm covers every protocol and operation shape. Modeled-error
handling is a single `catch` filtered by `TrySerializeError`, not a generated
`catch` block per error.

## Host Adapter

A host adapter binds a host framework to the runtime. It owns two conversions —
the host request into a `SmithyHttpRequest`, and a `SmithyHttpServerResponse` onto
the host response — and one dispatch entry point that generated endpoints call:

The runtime is currently stateless, so the host adapter owns a shared default
instance rather than requiring it in DI; generated endpoints never reference it.

```csharp
public static class SmithyAspNetCoreHost
{
    private static readonly SmithyServerRuntime Runtime = new();

    public static async Task DispatchAsync<TInput, TOutput>(
        HttpContext context,
        IServerOperationProtocol<TInput, TOutput> protocol,
        Func<TInput, CancellationToken, Task<TOutput>> handler,
        CancellationToken cancellationToken)
    {
        var request = await ToSmithyRequestAsync(context, cancellationToken).ConfigureAwait(false);
        var response = await Runtime.DispatchAsync(protocol, request, handler, cancellationToken).ConfigureAwait(false);
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

## Generated Endpoint

A generated endpoint is a route bound to a handler method and the operation's
protocol, delegating to the host adapter:

```csharp
endpoints.MapMethods("/foo", ["POST"], (HttpContext context, IFooHandler handler, CancellationToken ct) =>
    SmithyAspNetCoreHost.DispatchAsync(context, FooProtocol, handler.FooAsync, ct));
```

The only per-operation variation in codegen is the handler-adapter lambda (by
input/output presence and stream direction), the route and URI derived from the
model, and any static query validation from `@http`. The dispatch algorithm is
not generated.

## Multiple Protocols per Service

A service can be exposed over several protocols at once — restJson1, rpcv2Cbor,
gRPC — from **one set of handlers**. This is a consequence of the layering, not a
bolted-on feature:

- The handler interface is protocol-agnostic. `IFooHandler.FooAsync(FooInput, ct)`
  deals only in typed model values; there is one handler interface per service,
  derived from the operation shapes.
- All protocol knowledge lives in the operation-protocol binding.
  `new RestJson1Protocol().ForService(schema).ForOperation(op)` and
  `new RpcV2CborProtocol().ForService(schema).ForOperation(op)` are two different
  `IServerOperationProtocol<FooInput, FooOutput>` over the same typed contract.
- Dispatch is parameterized by that binding, so the same handler delegate serves
  every protocol.

Codegen emits one `Map{Service}{Protocol}` endpoint extension per protocol trait
on the service, each building its own bindings and all resolving the same
DI-registered handler:

```csharp
app.MapWeatherRestJson();    // @http routes:      POST /forecast
app.MapWeatherRpcV2Cbor();   // structured routes: /service/Weather/operation/GetForecast
services.AddWeatherHandler<MyWeatherHandler>();   // one handler, both
```

Two rules keep the handler shared across protocols:

- The handler interface's streaming surface derives from the model (`@streaming`),
  not from any one protocol. Event framing differs per protocol (vnd.amazon.eventstream
  versus gRPC's length prefix), but that lives entirely inside the binding; the
  handler surface `IAsyncEnumerable<TEvent>` is identical across protocols.
- Modeled errors and their status codes come from the schema, so each protocol
  binding serializes the same thrown exception its own way.

### Listeners and Ports

Generated `Map{Service}{Protocol}` extensions are port-agnostic — ports are a
deployment concern, not a model concern, so codegen never binds one. Whether two
protocols can share a listener depends on their routes and transport:

- **Disjoint routes share a port.** restJson1 (`@http` paths), rpcv2Cbor
  (`/service/…/operation/…`), and the awsJson family (`POST /` with `X-Amz-Target`)
  occupy different route shapes, so any mix of these three coexists on one
  listener; a client selects its protocol by which route it calls.
- **Same-route protocols need separate listeners.** awsJson1_0 and awsJson1_1
  both bind `POST /` and differ only by `Content-Type`. Generated routing does not
  dispatch on `Content-Type`, so exposing both requires pinning each to its own
  port with `RequireHost`.
- **Transport-incompatible protocols need separate listeners.** gRPC requires
  HTTP/2; it goes on its own HTTP/2 listener rather than sharing an HTTP/1.1 port.

The scoping mechanism is ASP.NET Core's `RequireHost`, applied by the app, not the
generator:

```csharp
app.MapWeatherGrpc().RequireHost("*:5002");   // gRPC on its own HTTP/2 port
```

## Streaming Type Naming

Request and response streaming are independent axes — a request may stream while
its response is unary, and vice versa — so the transport types are named for
what actually streams, not bundled under a single "duplex" name.

A streaming request body is a variant of the body union:

```csharp
public abstract record SmithyHttpBody
{
    public static SmithyHttpBody Empty { get; }
    public sealed record Bytes(byte[] Content) : SmithyHttpBody;
    public sealed record Streaming(System.IO.Stream Content, long? ContentLength = null) : SmithyHttpBody;
    public sealed record EventStreaming(IAsyncEnumerable<ReadOnlyMemory<byte>> Content) : SmithyHttpBody;
}
```

Every client request is a `SmithyHttpRequest`; the body says whether it streams:

- output-stream request = `SmithyHttpRequest { Body = Bytes }` — a unary request.
- input-stream and duplex request = `SmithyHttpRequest { Body = EventStreaming }`.

The response is a single `SmithyHttpClientResponse`. The client transport requires the
runtime to state whether the response body should be buffered or streamed:

```csharp
public interface IHttpTransport
{
    Task<SmithyHttpClientResponse> SendAsync(
        SmithyHttpRequest request,
        SmithyHttpClientResponseMode responseMode,
        CancellationToken cancellationToken = default);
}
```

In `Buffer` mode, `SmithyHttpClientResponse.Body` is `Bytes` or `Empty`, and trailers
are available through `Trailer` because the body has already been read. In
`Stream` mode, `Body` is `SmithyHttpBody.Streaming`, `Trailer` resolves HTTP
trailing headers once the body is read to end, and disposing the stream releases
the connection. Every client streaming half has a uniform signature: a
`SmithyHttpRequest` in, a `SmithyHttpClientResponse` out. The deserialize method's
return type (`TOutput` versus `IAsyncEnumerable<TOutputEvent>`) reflects the
payload shape.

## Non-goals

- Protocol-specific types (gRPC status codes, trailer names, media types) do not
  appear in the host adapter or generated server code.
- Codegen does not own the dispatch algorithm; the runtime is its single owner.
- Server interceptors and telemetry are out of scope for the initial runtime; the
  runtime only provides the seam where they attach later, as on the client.

## Related Docs

- [client-architecture.md](client-architecture.md) — the client stack this mirrors
- [streaming.md](streaming.md) — event-stream protocol interfaces and framing
- [http-interfaces.md](http-interfaces.md) — transport abstractions
- [codegen-architecture.md](codegen-architecture.md) — codegen pipeline
