---
title: Dependency Injection
description: Register a generated NSmithy client with IHttpClientFactory and the .NET service container.
---

NSmithy can generate an `Add{Service}Client` extension that registers the
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

This flows into the generated `smithy-build.json` so the extension is **only
generated when enabled** (it isn't emitted otherwise). If your project uses an
explicit `smithy-build.json` instead of one synthesized from a contracts
reference, set it on the plugin directly:

```json
{ "plugins": { "csharp-codegen": { "service": "...", "generateDependencyInjection": true } } }
```

It's opt-in because the generated code depends on `Microsoft.Extensions.Http`.
Reference that package (or the `Microsoft.AspNetCore.App` shared framework, which
already provides it) when you enable it.

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
(logging, resilience, auth handlers) and other typed-client configuration:

```csharp
services.AddWeatherClient(new Uri("https://api.example.com"))
    .AddHttpMessageHandler<AuthHandler>();
```

If your application owns `HttpClient` setup, use the callback overload. This is
where you set `BaseAddress`, timeouts, default headers, HTTP version policy, and
other `System.Net.Http.HttpClient` settings:

```csharp
services.AddWeatherClient(client =>
{
    client.BaseAddress = new Uri("https://api.example.com");
    client.Timeout = TimeSpan.FromSeconds(10);
});
```

## Configuring the client

NSmithy client options are configured separately from `HttpClient` options. The
config callback uses the same `{Service}ClientConfig` that constructors take (see
[Client Configuration](/smithy-dotnet/guides/client-configuration/)):

```csharp
services.AddWeatherClient(
    new Uri("https://api.example.com"),
    config => config.Protocol = new GrpcProtocol());
```

You can combine both callbacks when you need both layers:

```csharp
services.AddWeatherClient(
    client =>
    {
        client.BaseAddress = new Uri("https://api.example.com");
        client.Timeout = TimeSpan.FromSeconds(10);
    },
    config =>
    {
        config.Protocol = new GrpcProtocol();
        config.AuthSchemes.Add(authScheme);
    });
```

The helper configures the `HttpClient` for HTTP/2 automatically when the chosen
protocol requires it (native gRPC) — handling the one detail that is easy to get
wrong by hand.

## Manual registration

The generated helper is preferred. It configures the typed client and applies
protocol-specific `HttpClient` settings such as HTTP/2 for gRPC.

Generated clients are still plain typed clients, so manual registration works:

```csharp
services.AddHttpClient<IWeatherClient, WeatherClient>(client =>
    client.BaseAddress = new Uri("https://api.example.com"));
```

Use this only when you need full control over `IHttpClientFactory` registration.
For non-default protocols or gRPC, prefer `AddWeatherClient(...)`; otherwise you
must configure the `HttpClient` version and policy yourself.

## Constructors

Outside of DI, the generated `{Service}Client` is constructed from a
direct endpoint argument plus an optional `{Service}ClientConfig`, or from an
`HttpClient` / invoker plus an optional config. See
[Client Configuration](/smithy-dotnet/guides/client-configuration/).
