---
title: Client configuration
description: Construct and configure generated NSmithy clients.
---

Every service generates a concrete `{Service}Client` and an `I{Service}Client`
interface. You can construct the client directly or register it with the .NET
service container.

## Basic construction

For the common case, pass the endpoint directly:

```csharp
var client = new WeatherClient(new Uri("https://api.example.com"));
```

Add a `{Service}ClientConfig` when you need NSmithy-level options such as
protocol selection, auth, retry, interceptors, or idempotency tokens:

```csharp
using NSmithy.Protocols.Grpc;

var client = new WeatherClient(
    new Uri("https://api.example.com"),
    new()
    {
        Protocol = new GrpcProtocol(),
        IdempotencyTokenProvider = () => Guid.NewGuid().ToString(),
    });
```

The endpoint argument wins over `config.Endpoint` when both are supplied.

## Configuration object

`{Service}ClientConfig` is a per-service subclass of `SmithyClientConfig`. The
endpoint constructor fills `Endpoint` for you; pass config as the second argument
when you want to set client options.

| Option | Purpose |
| --- | --- |
| `Endpoint` | The service endpoint. Set by the endpoint constructor, and optional for the `HttpClient` constructor. |
| `Protocol` | The wire protocol. Defaults to the service's primary declared [protocol](/smithy-dotnet/protocols/overview/). |
| `EndpointResolver` | Per-operation endpoint resolution. Overrides the static `Endpoint` for request routing; can vary the endpoint by operation, add endpoint headers, and narrow auth schemes. |
| `DisableHostPrefixInjection` | Disables generated `@endpoint(hostPrefix)` / `@hostLabel` expansion. |
| `UserAgent` | User-Agent used when the modeled request does not set one. Defaults to `NSmithy.Client/{version}`. |
| `AuthSchemes` | Available auth implementations. Selection uses the operation’s effective modeled schemes and configured implementations; see [Authentication](/smithy-dotnet/guides/client-configuration/authentication/). |
| `RetryStrategy` | Runtime-owned retry policy. `null` disables runtime retries. |
| `OperationTimeout` | Deadline for one operation execution, spanning all retry attempts and backoff delays. Throws `TimeoutException` when exceeded; `null` (default) means no deadline. |
| `Interceptors` | Protocol-agnostic hooks for observing and modifying client execution. |
| `IdempotencyTokenProvider` | Overrides the idempotency-token generator (default: a random GUID). |

Configuration options are optional; supply an endpoint through the constructor,
configuration, or the provided `HttpClient`.

## Transport and lifetime

For direct construction, dispose the client when its lifetime ends:

```csharp
using var client = new WeatherClient(new Uri("https://api.example.com"));
```

For application services, use [dependency injection](/smithy-dotnet/guides/client-configuration/dependency-injection/).
See [Transport](/smithy-dotnet/guides/client-configuration/transport/) for constructor
variants, endpoint precedence, and ownership of supplied transports.

## Topics

- [Authentication](/smithy-dotnet/guides/client-configuration/authentication/)
  covers HTTP auth schemes and early-preview AWS SigV4 signing.
- [Retry](/smithy-dotnet/guides/client-configuration/retry/) covers
  runtime-owned retry configuration.
- [Interceptors](/smithy-dotnet/guides/client-configuration/interceptors/)
  covers client execution hooks.
- [Observability](/smithy-dotnet/guides/client-configuration/observability/)
  covers OpenTelemetry tracing and metrics.
- [Pagination](/smithy-dotnet/guides/client-configuration/pagination/)
  covers the generated `IAsyncEnumerable` paginators for `@paginated`
  operations.
- [Transport](/smithy-dotnet/guides/client-configuration/transport/) covers
  endpoint, `HttpClient`, and low-level runtime ownership.
- [Dependency Injection](/smithy-dotnet/guides/client-configuration/dependency-injection/)
  covers `Add{Service}Client`, `IHttpClientFactory`, and typed-client lifetime.
