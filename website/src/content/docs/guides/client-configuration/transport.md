---
title: Transport
description: Choose who owns the generated client's HTTP transport.
---

The generated client constructors differ by transport ownership:

```csharp
new WeatherClient(endpoint, config);      // client creates and owns HttpClient
new WeatherClient(httpClient, config);    // caller owns HttpClient
new WeatherClient(runtime, config);       // caller owns the lower-level runtime path
```

Use the endpoint constructor for normal direct construction:

```csharp
using var client = new WeatherClient(new Uri("https://api.example.com"));
```

When the client creates the `HttpClient`, disposing the generated client also
disposes that `HttpClient`. When you pass an `HttpClient` or runtime, disposal is
a no-op for the supplied transport.

## Bring your own HttpClient

Use the `HttpClient` constructor when another part of the application owns HTTP
configuration:

```csharp
using var httpClient = new HttpClient
{
    BaseAddress = new Uri("https://api.example.com"),
    Timeout = TimeSpan.FromSeconds(10),
};

var client = new WeatherClient(httpClient);
```

If both `config.Endpoint` and `HttpClient.BaseAddress` are set,
`config.Endpoint` wins.

For long-lived applications, prefer the generated
[`Add{Service}Client`](/smithy-dotnet/guides/client-configuration/dependency-injection/)
helper. It uses `IHttpClientFactory` and applies protocol-specific HTTP settings,
including HTTP/2 for native gRPC.

## Custom runtime

The runtime constructor is for custom transports and low-level tests. It bypasses
generated transport setup, so prefer the endpoint, `HttpClient`, or DI
constructors unless you need to own the lower-level runtime path.
