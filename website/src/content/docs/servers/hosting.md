---
title: Hosting & Multiple Protocols
description: Expose one service over several protocols from a single handler set, and the listener/port rules that decide what can share a port.
---

A service can declare more than one protocol trait. When it does, codegen emits
a `Map{Service}{Protocol}` extension per protocol, and every one of them
resolves the **same** handler you registered with `Add{Service}Handler`. The
handler deals only in model types, so nothing about it is protocol-specific.

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddWeatherServiceHandler<WeatherHandler>();   // one handler

var app = builder.Build();
app.MapWeatherServiceRestJson1();    // POST /forecast
app.MapWeatherServiceRpcV2Cbor();    // /service/Weather/operation/GetForecast
app.Run();
```

A client picks the protocol by which endpoint it calls; the wire serialization
lives entirely in each protocol's binding.

## What can share a port

Endpoints are port-agnostic — ports are a deployment concern, so the generated
`Map` extensions never bind one. Whether two protocols can share a listener
depends on their routes and transport:

- **Disjoint routes share a port.** restJson1 (`@http` paths), rpcv2Cbor
  (`/service/…/operation/…`), and the awsJson family (`POST /` with
  `X-Amz-Target`) occupy different route shapes, so any mix of these coexists on
  one listener.
- **Same-route protocols need separate listeners.** `awsJson1_0` and
  `awsJson1_1` both bind `POST /` and differ only by `Content-Type`. Generated
  routing does not dispatch on `Content-Type`, so exposing both means pinning
  each to its own port.
- **gRPC needs its own listener.** gRPC requires HTTP/2; cleartext gRPC does not
  share an HTTP/1.1 port reliably. Give it a dedicated HTTP/2 port.

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
app.MapWeatherServiceRestJson1();
app.MapWeatherServiceGrpc().RequireHost("*:5001");
app.Run();
```

See [gRPC](/smithy-dotnet/protocols/grpc/) for the full gRPC hosting example.
