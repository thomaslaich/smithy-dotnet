---
title: Dependency injection
description: Register a generated NSmithy client with IHttpClientFactory and the .NET service container.
---

NSmithy can generate an `Add{Service}Client` extension that registers the
client as a typed [`IHttpClientFactory`](https://learn.microsoft.com/aspnet/core/fundamentals/http-requests)
client. This is the recommended way to use a client with the .NET service
container.

## Setup

Enable the helper in the client project's `smithy-build.json`, next to its
`.csproj`. A client that generates from a contracts reference adds the file with
only this setting:

```json title="smithy-build.json"
{
  "version": "1.0",
  "plugins": { "csharp-codegen": { "generateDependencyInjection": true } }
}
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

The helper applies the service protocol's modeled `http` / `eventStreamHttp`
preference to the `HttpClient`, including downgrade policy. The
`configureClient` callback runs afterwards and can override that setup. Native
gRPC defaults to exact HTTP/2.

## Resolving configuration dependencies

Use the service-provider overload when configuration needs registered dependencies:

```csharp
services.AddScoped<CompanyEndpointResolver>();
services.AddWeatherClient(
    (provider, config) =>
    {
        config.EndpointResolver = provider.GetRequiredService<CompanyEndpointResolver>();
    },
    configureClient: client => client.Timeout = TimeSpan.FromSeconds(10));
```

The callback runs each time a typed client is created, with a fresh config and
the provider that resolves the client. Scoped dependencies come from that scope,
not from the scope `IHttpClientFactory` uses for pooled handlers. With a custom
endpoint resolver, neither `BaseAddress` nor `Endpoint` is needed.

This overload applies the protocol's HTTP defaults when the typed client is
created, after the factory's `HttpClient` callbacks. `configureClient` runs last
and can override them.

## Manual registration

The generated helper is preferred. It configures the typed client and applies
protocol-specific `HttpClient` version preferences.

Generated clients are still plain typed clients, so manual registration works:

```csharp
services.AddHttpClient<IWeatherClient, WeatherClient>(client =>
    client.BaseAddress = new Uri("https://api.example.com"));
```

Use this only when you need full control over `IHttpClientFactory` registration.
For non-default protocols or gRPC, prefer `AddWeatherClient(...)`; otherwise you
must configure the `HttpClient` version and policy yourself.
