---
title: Hosting multiple protocols
description: Expose one service over several protocols from a single handler set, and the listener/port rules that decide what can share a port.
---

A service can declare more than one protocol trait. When it does, codegen emits
a `Map{Service}` extension with a generated `{Service}Protocols` flags enum.
Each selected protocol resolves the registered operation handlers. Their method
signatures use the same model types across protocols.

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddWeatherServiceHandler<WeatherHandler>();   // one handler

var app = builder.Build();
app.MapWeatherService(
    WeatherServiceProtocols.RestJson1 | WeatherServiceProtocols.RpcV2Cbor);
app.Run();
```

The client must select the matching protocol as well as the endpoint.

## Server runtime registration

`Add{Service}Handler<THandler>()` also registers a default `SmithyServerRuntime`.
Endpoints resolve it from request services and pass it to the ASP.NET Core host
adapter. An existing application registration takes precedence.

If you register operation handlers individually, register the runtime explicitly:

```csharp
using NSmithy.Server.AspNetCore;

builder.Services.AddSmithyServer();
builder.Services.AddScoped<IGetWeatherHandler, GetWeatherHandler>();
```

The runtime currently has no configurable lifecycle options. DI establishes its
ownership and lifetime; it does not add interceptors or telemetry by itself.

## What can share a port

Endpoints are port-agnostic — ports are a deployment concern, so the generated
`Map` extension never binds one. Whether two protocols can share a listener
depends on their routes and transport:

- Protocols can share a listener when their routes do not conflict. REST paths
  are model-defined, so check them against RPC or gRPC dispatch paths.
- Conflicting routes need separate host or port constraints; the generated
  router does not distinguish protocols by `Content-Type`.
- gRPC requires HTTP/2. Use a dedicated HTTP/2 listener for cleartext development.
  With TLS, HTTP/1.1 and HTTP/2 can share a listener through ALPN negotiation.

## Pinning a protocol to a port

Use ASP.NET Core's `RequireHost` to scope a protocol's endpoints to a specific
port, and configure Kestrel to listen there:

```csharp
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5000);                                   // HTTP/1.1 — REST
    options.ListenLocalhost(5001, o => o.Protocols = HttpProtocols.Http2); // gRPC
});

var app = builder.Build();
app.MapGroup("")
    .RequireHost("*:5000")
    .MapWeatherService(WeatherServiceProtocols.RestJson1);
app.MapGroup("")
    .RequireHost("*:5001")
    .MapWeatherService(WeatherServiceProtocols.Grpc);
app.Run();
```

See [gRPC](/smithy-dotnet/protocols/grpc/) for the full gRPC hosting example.
