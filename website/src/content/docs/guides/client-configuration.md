---
title: Client Configuration
description: Construct and configure a generated NSmithy client — protocol, auth, middleware, and lifetime.
---

Every service generates a concrete `{Service}Client` and an `I{Service}Client`
interface. You can construct the client directly (shown here) or register it with
the .NET service container — see
[Dependency Injection](/smithy-dotnet/guides/dependency-injection/).

## Basic construction

For the common case, pass the endpoint directly:

```csharp
var client = new WeatherClient(new Uri("https://api.example.com"));
```

Add a `{Service}ClientConfig` when you need NSmithy-level options such as
protocol selection, auth, middleware, or idempotency tokens:

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

## Configuration

`{Service}ClientConfig` is a per-service subclass of `SmithyClientConfig`. The
endpoint constructor fills `Endpoint` for you; pass config as the second argument
when you want to set client options:

```csharp
using NSmithy.Client;
using NSmithy.Protocols.Grpc;

var client = new WeatherClient(
    new Uri("https://api.example.com"),
    new()
    {
        Protocol = new GrpcProtocol(),
        IdempotencyTokenProvider = () => Guid.NewGuid().ToString(),
    });
```

| Option | Purpose |
| --- | --- |
| `Endpoint` | The service endpoint. Set by the endpoint constructor, and optional for the `HttpClient` constructor. |
| `Protocol` | The wire protocol. Defaults to the service's primary declared [protocol](/smithy-dotnet/protocols/overview/). |
| `AuthSchemes` | Configured auth schemes; the resolver installs the first scheme the service models. An empty list means anonymous. |
| `Middleware` | Extra `ISmithyClientMiddleware` prepended to the operation pipeline. |
| `IdempotencyTokenProvider` | Overrides the idempotency-token generator (default: a random GUID). |

Everything except `Endpoint` is optional. The config is a per-service type
(`WeatherClientConfig`), so service-specific options can be added later without
changing the constructor signature.

### Auth example

Clients take auth through `AuthSchemes`. For example, early-preview AWS SigV4
signing is configured like this:

```csharp
using NSmithy.Aws;

var credentials = new StaticAwsCredentialsProvider(new AwsCredentials("ak", "sk"));

var dynamoDb = new DynamoDB20120810Client(
    new Uri("http://localhost:4566"),
    new()
    {
        AuthSchemes = { new AwsSigV4AuthScheme("dynamodb", "us-east-1", credentials) },
    });
```

See [Authentication](/smithy-dotnet/guides/authentication/) for the full auth
configuration guide and current AWS SigV4 limitations.

## Constructors

The public constructors differ mainly by who owns the HTTP transport:

```csharp
new WeatherClient(endpoint, config);      // normal direct construction; endpoint wins over config.Endpoint
new WeatherClient(httpClient, config);    // you own the HttpClient; endpoint from config.Endpoint ?? BaseAddress
new WeatherClient(invoker, config);       // you own the whole transport/middleware pipeline
```

`config` is optional on all public constructors.

Keep the constructor choice simple:

- Use the endpoint constructor for normal application code.
- Use the generated `Add{Service}Client` helper for dependency injection.
- Use the `HttpClient` constructor only when something else already owns and
  configures the `HttpClient` — for example manual typed-client registration,
  tests with a custom `HttpMessageHandler`, or a shared client with handlers set
  up outside NSmithy.
- Use the invoker constructor only for custom transports, custom middleware
  pipelines, and low-level tests. It bypasses generated transport setup; put
  auth and middleware into the invoker pipeline instead of `config.AuthSchemes`
  or `config.Middleware`.

## Lifetime

The client implements `IDisposable`. When the client creates the `HttpClient`
itself (the endpoint constructor), `Dispose` releases it. When you supply an
`HttpClient` or invoker, `Dispose` is a no-op, so the transport you own is never
closed:

```csharp
using var client = new WeatherClient(new Uri("https://api.example.com"));
```

For a long-lived application, prefer registering the client once with
`IHttpClientFactory` over constructing one per call.

## Dependency injection

For the .NET service container, generate an `Add{Service}Client` extension
instead of constructing by hand. It registers the client as a typed
`IHttpClientFactory` client and configures HTTP/2 for gRPC automatically. The
same `{Service}ClientConfig` is set through a callback:

```csharp
services.AddWeatherClient(
    new Uri("https://api.example.com"),
    config => config.Protocol = new GrpcProtocol());
```

See [Dependency Injection](/smithy-dotnet/guides/dependency-injection/) for the
full setup, protocol selection, and manual registration.
