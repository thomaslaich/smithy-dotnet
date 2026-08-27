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
    Task<SmithyHttpClientResponse> SendAsync(
        SmithyHttpRequest request,
        SmithyHttpClientResponseMode responseMode,
        CancellationToken cancellationToken = default
    );
}
```

`HttpClientTransport` is the default implementation, wrapping `System.Net.Http.HttpClient`.

## Request and Response

`SmithyHttpRequest` carries:

- `HttpMethod Method`
- `string RequestUri` — resolved against the client endpoint by the transport
- `IDictionary<string, IReadOnlyList<string>> Headers` (multi-value)
- `byte[]? Content`, `string? ContentType`, and a separate `ContentHeaders`

`SmithyHttpClientResponse` carries:

- `HttpStatusCode StatusCode` and `string? ReasonPhrase`
- `byte[] Content` (with a `ContentText` convenience accessor)
- `IReadOnlyDictionary<string, IReadOnlyList<string>> Headers` and `ContentHeaders`
- `Func<string, string?>? Trailer` for trailing response headers

These types are deliberately flat. HTTP/2 trailers (e.g. gRPC's `grpc-status`) are
kept separate from upfront response headers and exposed through `Trailer`. Bindings
that need the status (e.g. `@httpResponseCode`) read `StatusCode` directly.

## Client Construction

Generated clients expose constructors rather than a builder. The common path is
endpoint-first, with an optional per-service config object for protocol, auth,
interceptors, retry, and future client knobs:

```csharp
new WeatherClient(endpoint);                                  // default config
new LibraryServiceClient(endpoint, new() { Protocol = new GrpcProtocol() });
new WeatherClient(httpClient, config);                        // endpoint from config.Endpoint ?? BaseAddress
new WeatherClient(runtime, config);                           // custom transport/interceptors/testing
```

The generated `{Service}ClientConfig : SmithyClientConfig` is the canonical
configuration model. It currently carries `Endpoint`, `Protocol`, `AuthSchemes`,
`Interceptors`, `RetryStrategy`, and `IdempotencyTokenProvider`; service-specific
and future runtime options can be added as properties without changing
constructor signatures. The endpoint constructor writes the positional endpoint
into config and then delegates to a private config constructor. The positional
endpoint wins over any `config.Endpoint` value.

When the caller supplies no `HttpClient`, the client creates one using the
protocol trait's modeled `http` / `eventStreamHttp` preference and downgrade
policy. Native gRPC defaults to exact HTTP/2.

The protocol implementation composes the transport with the codec and the
protocol binding to produce a complete request pipeline. The generated client
passes operation schemas and typed values into the protocol adapter; for the
endpoint/HttpClient constructors it wraps the `HttpClient` in an
`IHttpTransport` (`HttpClientTransport`) internally. The client runtime applies
the effective endpoint to relative request URIs before handing the request to the
transport, so `HttpClientTransport` sends the URI it receives.

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

Both are supported. The `HttpClient` constructor is kept so a client can be
registered as a typed client and the factory can own the `HttpClient` (pooled
handlers, Polly, DNS refresh):

```csharp
services.AddHttpClient<IWeatherClient, WeatherClient>(c =>
    c.BaseAddress = new Uri("https://api.example.com"));
```

Consumers who need a fully custom transport instead implement `IHttpTransport`,
build a `SmithyClientRuntime`, and pass it to the runtime constructor. That
constructor is intentionally lower-level: the runtime already owns the transport
path, so generated-client config auth/interceptors do not apply there.

## URI Construction

REST protocol implementations build the request URI in two layers:

1. The protocol builds the operation-relative URI template (from `@http`), with
   `@httpLabel` members substituted and `@httpQuery` / `@httpQueryParams`
   members appended.
2. The client runtime resolves that relative URI against the effective endpoint
   (`config.Endpoint`, the endpoint constructor argument, or
   `HttpClient.BaseAddress` for bring-your-own-`HttpClient` construction).

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
their body codec factory, backed by `JsonCodecFactory` or `XmlCodecFactory`.

This split lets other protocols share the HTTP transport when appropriate
without inheriting REST binding semantics. A protocol that serializes the whole
operation input into one body can ignore REST traits even if the schema graph
carries them.

## gRPC

gRPC is just another `IProtocol` (`GrpcProtocol`) over this same transport: it uses
an exact HTTP/2 `HttpVersionPreference` but otherwise uses `HttpClientTransport` like the
REST and rpcv2Cbor protocols, with trailers exposed through `SmithyHttpClientResponse.Trailer`.
The wire format, framing, proto codec, and error model are covered in
[native-grpc.md](native-grpc.md).

## Related Docs

- [native-grpc.md](native-grpc.md) — native gRPC protocol and proto codec
- [serialization.md](serialization.md) — codec and protocol binding
- [codegen-architecture.md](codegen-architecture.md) — codegen pipeline
