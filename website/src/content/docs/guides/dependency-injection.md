---
title: Dependency Injection
description: Register a generated NSmithy client with IHttpClientFactory and the .NET service container.
---

NSmithy can generate a turnkey `Add{Service}Client` extension that registers the
client as a typed [`IHttpClientFactory`](https://learn.microsoft.com/aspnet/core/fundamentals/http-requests)
client. This is the recommended way to use a client with the .NET service
container.

## Setup

Enable the helper in the client project:

```xml
<PropertyGroup>
  <SmithyGenerateDependencyInjection>true</SmithyGenerateDependencyInjection>
</PropertyGroup>
```

It's opt-in because the generated code depends on `Microsoft.Extensions.Http`.
Reference that package (or the `Microsoft.AspNetCore.App` shared framework, which
already provides it) when you enable the flag.

## Registering the client

```csharp
services.AddWeatherClient(new Uri("https://api.example.com"));
```

Then inject the interface anywhere:

```csharp
public sealed class ForecastService(IWeatherClient weather)
{
    public Task<GetForecastOutput> GetAsync(string city) =>
        weather.GetForecastAsync(new GetForecastInput(city));
}
```

`Add{Service}Client` returns the `IHttpClientBuilder`, so you can chain handlers
(retry, auth, logging) and any other typed-client configuration:

```csharp
services.AddWeatherClient(new Uri("https://api.example.com"))
    .AddHttpMessageHandler<AuthHandler>();
```

To configure the `HttpClient` yourself (Refit-style) instead of passing an
endpoint, use the callback overload:

```csharp
services.AddWeatherClient(client => client.BaseAddress = new Uri("https://api.example.com"));
```

## Choosing a protocol

The helper uses the service's default (primary) protocol. For a multi-protocol
service — for example one declaring both `@simpleRestJson` and `@grpc` — pass the
protocol you want:

```csharp
services.AddWeatherClient(new Uri("https://api.example.com"), protocol: new GrpcProtocol());
```

The helper configures the `HttpClient` for HTTP/2 automatically when the chosen
protocol requires it (native gRPC) — handling the one detail that is easy to get
wrong by hand.

## Manual registration

You don't need the helper — the generated client is a plain typed client, so the
standard pattern works:

```csharp
services.AddHttpClient<IWeatherClient, WeatherClient>(client =>
    client.BaseAddress = new Uri("https://api.example.com"));
```

This is fine, but **prefer the generated helper**: this form always uses the
default protocol, and — because `IHttpClientFactory` owns the `HttpClient` — it
cannot configure HTTP/2 for gRPC for you. If you go manual and need a non-default
protocol or HTTP/2, you have to drop down to a named client with an explicit
factory and configure the `HttpClient` version yourself:

```csharp
services.AddHttpClient(nameof(WeatherClient), client =>
    {
        client.BaseAddress = new Uri("https://api.example.com");
        client.DefaultRequestVersion = HttpVersion.Version20; // gRPC needs HTTP/2
    })
    .AddTypedClient<IWeatherClient>(static (http, _) =>
        new WeatherClient(http, protocol: new GrpcProtocol()));
```

— which is exactly the boilerplate `AddWeatherClient` generates for you.

## Constructors

Outside of DI, the generated `{Service}Client` exposes three constructors. Each
takes an optional `protocol` (defaulting to the service's primary protocol), plus
optional `middleware` / `idempotencyTokenProvider`:

```csharp
new WeatherClient(endpoint);            // client owns its HttpClient (HTTP/2 auto for gRPC)
new WeatherClient(httpClient);          // you own it; endpoint from BaseAddress
new WeatherClient(invoker);             // you own the whole transport/middleware pipeline

// the protocol is optional on every constructor:
new WeatherClient(endpoint, protocol: new GrpcProtocol());
```
