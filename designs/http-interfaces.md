# HTTP Interfaces

How NSmithy abstracts the HTTP transport layer.

## Goals

- Operation execution depends on `IHttpTransport`. Generated clients accept
  `HttpClient` for convenience; consumers can supply a runtime with a test double
  or another HTTP stack.
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
- `string RequestUri` — resolved against the effective endpoint by the client runtime
- `IDictionary<string, IReadOnlyList<string>> Headers` (multi-value)
- `SmithyHttpBody Body` and `string? ContentType`

`SmithyHttpClientResponse` carries:

- `HttpStatusCode StatusCode` and `string? ReasonPhrase`
- `SmithyHttpBody Body`; `Content` / `ContentText` are buffered-body convenience accessors
- `IReadOnlyDictionary<string, IReadOnlyList<string>> Headers` and `ContentHeaders`
- `Func<string, string?>? Trailer` for trailing response headers

`SmithyHttpBody` distinguishes empty, buffered byte, live stream, and framed
async-enumerable bodies. The client runtime selects buffered or streaming
response mode from the operation binding. HTTP/2 trailers (e.g. gRPC's `grpc-status`) are
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
constructor signatures. The endpoint constructor shallow-copies config, writes
the positional endpoint into that copy, and delegates to a private constructor. The positional
endpoint wins over any `config.Endpoint` value.

When the caller supplies no `HttpClient`, the client creates one using the
protocol trait's modeled `http` / `eventStreamHttp` preference and downgrade
policy. Native gRPC defaults to exact HTTP/2.

`SmithyHttpClientEnvironment` binds the service protocol, resolves configured
auth schemes, wraps the HTTP client in `HttpClientTransport`, and constructs
`SmithyClientRuntime`. Generated code supplies service defaults and operation
bindings. The environment disposes only the HTTP client it created, including
when construction fails; supplied clients and runtimes remain caller-owned.

The protocol owns serialization and wire rules. The runtime owns endpoint
resolution, authentication, retries, interceptors, and transport invocation. It
resolves relative request URIs before handing them to the transport.

## Why a Transport Interface

`IHttpTransport` makes an individual HTTP attempt independently replaceable and
testable. Protocol tests can inspect neutral requests and provide neutral
responses without hosting a server. `HttpClientTransport` can also be tested
through a custom `HttpMessageHandler`; no mocking library is required.

Retries belong to `SmithyClientRuntime`, where authentication, operation deadlines,
and body replayability can be considered together. A transport implements one
attempt and does not need to know about Smithy operation lifecycles.

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

`NSmithy.Http` defines neutral HTTP messages, transport, and schema-bound
protocol interfaces. Concrete protocol packages interpret Smithy traits; the
transport does not.

REST-specific Smithy bindings live in `NSmithy.Protocols.Rest`. That package
projects operation input and output schemas into HTTP method, URI labels, query
parameters, headers, payload members, response status, and body members.
Protocols such as REST JSON and REST XML reuse that projection and provide only
their body codec factory, backed by `JsonCodecFactory` or `XmlCodecFactory`.

This split lets other protocols share the HTTP transport when appropriate
without inheriting REST binding semantics. A protocol that serializes the whole
operation input into one body can ignore REST traits even if the schema graph
carries them.

## Protocol Binding Lifetimes

Protocol construction has three stages:

```text
IProtocol
    -> IServiceProtocol
        -> IClientOperationProtocol<TInput, TOutput>
        -> IServerOperationProtocol<TInput, TOutput>
```

The unbound protocol carries configuration. The service-bound protocol compiles
and caches service information. Operation protocols compile bindings, codecs,
validation, and error behavior once per operation and call side. Keep these
stages unless measurement shows that a service-bound layer has no meaningful
shared state. `IOperationProtocol<TInput, TOutput>` combines the two operation
interfaces for implementation convenience; runtimes depend on their own half.

Despite their broad names, these interfaces, `SmithyOperationBinding`, and
`SmithyClientRuntime` describe HTTP exchanges. gRPC fits because it uses HTTP/2.
Durable broker consumption needs its own execution model. HTTP-specific names
such as `IHttpProtocol` or `SmithyHttpClientRuntime` would make that boundary more
explicit, but a public rename is deferred to a planned breaking release.

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
