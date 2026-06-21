# HTTP Interfaces

How NSmithy abstracts the HTTP transport layer.

## Goals

- Generated clients must not depend on `HttpClient` or any specific .NET HTTP
  implementation. Consumers should be able to substitute a different transport
  (e.g. a test double, a custom retry wrapper, or a non-`HttpClient` HTTP stack).
- The transport interface must be minimal: one method, one request type, one
  response type.
- Request and response types must carry enough information for any Smithy HTTP
  protocol binding (headers, body, status code, URI).
- The interface should be async-first and cancellation-aware.

## Transport Interface

`IHttpTransport` (in `NSmithy.Http`) is the core abstraction:

```csharp
public interface IHttpTransport
{
    Task<SmithyHttpResponse> SendAsync(
        SmithyHttpRequest request,
        CancellationToken cancellationToken = default
    );
}
```

`HttpClientTransport` is the default implementation, wrapping `System.Net.Http.HttpClient`.

## Request and Response

`SmithyHttpRequest` carries:

- `HttpMethod Method`
- `Uri Uri`
- `IReadOnlyDictionary<string, string> Headers`
- `Stream? Body`

`SmithyHttpResponse` carries:

- `int StatusCode`
- `IReadOnlyDictionary<string, string> Headers`
- `Stream Body`

These types are deliberately flat. They do not model trailers, HTTP/2 push
promises, or other advanced HTTP features. Protocol bindings that need
additional information (e.g. `@httpResponseCode`) read `StatusCode` directly.

## Client Construction

Generated clients expose constructors rather than a builder (idiomatic C#; optional
and named parameters cover what a builder would). The protocol is chosen by an
optional `IProtocol` parameter, defaulting to the service's primary declared
protocol:

```csharp
new WeatherClient(endpoint);                                  // default (primary) protocol
new LibraryServiceClient(endpoint, protocol: new GrpcProtocol());
new WeatherClient(httpClient);                                // endpoint from httpClient.BaseAddress
new WeatherClient(invoker, new RestJson1Protocol());          // custom transport/middleware/DI
```

Cross-cutting configuration is passed as first-class parameters, not a single
options object — `middleware` (an `ISmithyClientMiddleware` pipeline) and, for
operations with `@idempotencyToken`, an `idempotencyTokenProvider`. When the caller
supplies no `HttpClient`, the client creates one, configured for HTTP/2 when the
protocol requires it (`IProtocol.RequiresHttp2`, true for native gRPC).

The protocol implementation composes the transport with the codec and the
protocol binding to produce a complete request pipeline. The generated client
passes operation schemas and typed values into the protocol adapter; for the
endpoint/HttpClient constructors it wraps the `HttpClient` in an
`IHttpTransport` (`HttpClientTransport`) internally.

## Why Not `HttpClient` Directly

Accepting `HttpClient` in generated clients is common in .NET codegen tools but
has several drawbacks for Smithy:

- **Protocol coupling**: generated clients would be tied to HTTP/1.1 and HTTP/2
  semantics. Smithy protocols like `rpcv2Cbor` or future transports would
  require a different abstraction anyway.
- **Testability**: replacing `HttpClient` with a test double requires
  `HttpMessageHandler` subclassing, which is not composable without a mocking
  library.
- **Flexibility**: `IHttpTransport` lets consumers wrap the transport with retry
  logic, logging, or a circuit breaker without patching the generated client.

## Relationship to `IHttpClientFactory`

`IHttpClientFactory` (ASP.NET Core DI) manages `HttpClient` lifetimes but returns
`HttpClient` instances; it does not provide a send-level abstraction. NSmithy's
`IHttpTransport` sits one level below: it models a single `send` call rather than a
named client.

Both are supported. The `HttpClient`-first constructor lets a client be registered
as a typed client so the factory owns the `HttpClient` (pooled handlers, Polly,
DNS refresh):

```csharp
services.AddHttpClient<IWeatherClient, WeatherClient>(c =>
    c.BaseAddress = new Uri("https://api.example.com"));
```

Consumers who need a fully custom transport instead implement `IHttpTransport` and
pass a `SmithyOperationInvoker` to the invoker constructor.

## URI Construction

REST protocol implementations build the request URI by combining:

1. The client's endpoint (the base URI passed at construction, or the
   `HttpClient.BaseAddress`).
2. The operation's URI template (from `@http`), with `@httpLabel` members
   substituted.
3. `@httpQuery` and `@httpQueryParams` members appended as query string
   parameters.

URI template expansion follows the rules in
[RFC 6570](https://www.rfc-editor.org/rfc/rfc6570) for the subset used by
Smithy HTTP traits.

## Protocol Layering

`NSmithy.Http` only models raw HTTP transport. It does not know about Smithy
traits or operation schemas.

REST-specific Smithy bindings live in `NSmithy.Protocols.Rest`. That package
projects operation input and output schemas into HTTP method, URI labels, query
parameters, headers, payload members, response status, and body members.
Protocols such as REST JSON and REST XML reuse that projection and provide only
their body codec factory, for example `JsonCodec` or `XmlCodec`.

This split lets other protocols share the HTTP transport when appropriate
without inheriting REST binding semantics. A protocol that serializes the whole
operation input into one body can ignore REST traits even if the schema graph
carries them.

## gRPC Transport

gRPC is a native NSmithy protocol (`GrpcProtocol` in `NSmithy.Protocols.Grpc`), not a
wrapper around `Grpc.Net.Client` / `Grpc.Tools`. It is an `IProtocol` like any other:
it reads and writes the gRPC wire format itself (5-byte length-prefixed framing,
`application/grpc+proto` body via the proto codec, `grpc-status` trailer error model)
over the same `HttpClientTransport` as the REST and rpcv2Cbor protocols — the only
difference is that it requires HTTP/2 (`IProtocol.RequiresHttp2`), which the generated
client configures on the `HttpClient` it creates. There is no `GrpcChannel`, no
generated `.proto` stub, and no protoc dependency on the client.

Because gRPC is a normal `IProtocol`, the same generated `{Service}Client` speaks it —
`new LibraryServiceClient(endpoint, protocol: new GrpcProtocol())` — with no separate
gRPC client type. Like rpcv2Cbor, it does not use the REST binding projection: the
operation payload is a single protobuf message body rather than REST labels, query
parameters, and headers.

## Related Docs

- [serialization.md](serialization.md) — codec and protocol binding
- [codegen-architecture.md](codegen-architecture.md) — codegen pipeline
- [Multi-Protocol Guide](/smithy-dotnet/multi-protocol/)
