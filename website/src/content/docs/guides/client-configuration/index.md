---
title: Client Configuration
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
| `AuthSchemes` | Configured auth schemes; the resolver installs the first scheme the service models. An empty list means anonymous. |
| `RetryStrategy` | Runtime-owned retry policy. `null` disables runtime retries. |
| `OperationTimeout` | Deadline for one operation execution, spanning all retry attempts and backoff delays. Throws `TimeoutException` when exceeded; `null` (default) means no deadline. |
| `Interceptors` | Protocol-agnostic hooks for observing and modifying client execution. |
| `IdempotencyTokenProvider` | Overrides the idempotency-token generator (default: a random GUID). |

Everything except `Endpoint` is optional. The config is a per-service type
(`WeatherClientConfig`), so service-specific options can be added later without
changing the constructor signature.

## Constructors

The public constructors differ mainly by who owns the HTTP transport:

```csharp
new WeatherClient(endpoint, config);      // normal direct construction; endpoint wins over config.Endpoint
new WeatherClient(httpClient, config);    // you own the HttpClient; endpoint from config.Endpoint ?? BaseAddress
new WeatherClient(runtime, config);       // you own the lower-level runtime path
```

`config` is optional on all public constructors.

Keep the constructor choice simple:

- Use the endpoint constructor for normal application code.
- Use the generated `Add{Service}Client` helper for dependency injection.
- Use the `HttpClient` constructor only when something else already owns and
  configures the `HttpClient`.
- Use the runtime constructor only for custom transports and low-level tests.

## Lifetime

The client implements `IDisposable`. When the client creates the `HttpClient`
itself, `Dispose` releases it. When you supply an `HttpClient` or runtime,
`Dispose` is a no-op, so the transport you own is never closed:

```csharp
using var client = new WeatherClient(new Uri("https://api.example.com"));
```

For a long-lived application, prefer registering the client once with
`IHttpClientFactory` over constructing one per call.

## Topics

- [Authentication](/smithy-dotnet/guides/client-configuration/authentication/)
  covers HTTP auth schemes and early-preview AWS SigV4 signing.
- [Retry](/smithy-dotnet/guides/client-configuration/retry/) covers
  runtime-owned retry configuration.
- [Interceptors](/smithy-dotnet/guides/client-configuration/interceptors/)
  covers client execution hooks.
- [Observability](/smithy-dotnet/guides/client-configuration/observability/)
  covers OpenTelemetry tracing and metrics.
- [Transport](/smithy-dotnet/guides/client-configuration/transport/) covers
  endpoint, `HttpClient`, and low-level runtime ownership.
- [Dependency Injection](/smithy-dotnet/guides/client-configuration/dependency-injection/)
  covers `Add{Service}Client`, `IHttpClientFactory`, and typed-client lifetime.
