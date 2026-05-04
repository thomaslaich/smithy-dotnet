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

Generated clients receive an `IHttpTransport` and a `SmithyClientOptions` at
construction time. `SmithyClientOptions` carries:

- `Uri Endpoint` — base endpoint for all operations.
- Auth configuration (future; currently unsigned).

The protocol implementation composes the transport with the codec and the
protocol binding to produce a complete request pipeline. The generated client
does not call `HttpClient` directly.

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

## Why Not `IHttpClientFactory`

`IHttpClientFactory` (ASP.NET Core DI) manages `HttpClient` lifetimes but still
returns `HttpClient` instances. It does not provide a send-level abstraction.

NSmithy's `IHttpTransport` sits one level below the factory: it models a single
`send` call rather than a named client. Consumers who want `IHttpClientFactory`
can wrap it in a custom `IHttpTransport` implementation.

## URI Construction

Protocol implementations build the request URI by combining:

1. `SmithyClientOptions.Endpoint` (the base URI).
2. The operation's URI template (from `@http`), with `@httpLabel` members
   substituted.
3. `@httpQuery` and `@httpQueryParams` members appended as query string
   parameters.

URI template expansion follows the rules in
[RFC 6570](https://www.rfc-editor.org/rfc/rfc6570) for the subset used by
Smithy HTTP traits.

## gRPC Transport

gRPC operations use a separate generated client that depends on the
`Grpc.Net.Client` package rather than `NSmithy.Http`. The generated `.proto`
file is compiled by Grpc.Tools into a gRPC stub; the NSmithy gRPC client wraps
that stub.

`IHttpTransport` is not used for gRPC operations. The two transports (HTTP and
gRPC) are selected at compile time by which generated client the consumer
instantiates.

## Related Docs

- [serialization.md](serialization.md) — codec and protocol binding
- [codegen-architecture.md](codegen-architecture.md) — codegen pipeline
- [Multi-Protocol Guide](../docs/multi-protocol.md)
