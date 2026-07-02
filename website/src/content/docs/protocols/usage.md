---
title: Client & Server Usage
description: The generated handler and client are the same across every protocol — only the model's protocol trait and the wire format change.
---

Application code does not change when the protocol does: you implement one
handler and call one typed client, and swapping the protocol trait on the
service swaps the wire format without touching either side.

This page is the canonical unary example. Each protocol page documents only
what is protocol-specific: the trait to apply, any modeling requirements, and
the bytes on the wire.

## Model

A single read operation. The shapes below — and the C# that follows — are the
same for every protocol. What changes per protocol is layered on top:

- the **service's protocol trait** (`@simpleRestJson`, `@restJson1`,
  `@rpcv2Cbor`, `@grpc`, …), and
- for HTTP protocols, the **HTTP binding traits** on operations (`@http`,
  `@httpLabel`, …); gRPC uses `@protoIndex` instead.

```smithy
$version: "2"

namespace example.weather

// Add a protocol trait here — @simpleRestJson, @restJson1, @rpcv2Cbor, @grpc, …
service Weather {
    version: "2026-01-01"
    operations: [GetCity]
}

@readonly
operation GetCity {
    input := {
        @required
        cityId: String
    }
    output := {
        @required
        name: String
    }
    errors: [NoSuchResource]
}

@error("client")
structure NoSuchResource {
    @required
    resourceType: String
}
```

A service generates nothing until it carries exactly one protocol trait, so this
neutral shape isn't buildable on its own — each protocol page shows the complete,
annotated model.

## Server

NSmithy generates one `IWeatherServiceHandler` interface with a method per
operation. Implement it once; the generated ASP.NET Core adapter handles
routing, serialization, and error dispatch.

```csharp
using Example.Weather;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddWeatherServiceHandler<WeatherHandler>();

var app = builder.Build();
app.MapWeatherServiceHttp();
app.Run();

internal sealed class WeatherHandler : IWeatherServiceHandler
{
    public Task<GetCityOutput> GetCityAsync(
        GetCityInput input, CancellationToken ct = default)
    {
        if (input.CityId == "unknown")
            throw new NoSuchResource(null, "City");

        return Task.FromResult(new GetCityOutput("Seattle"));
    }
}
```

Throwing a generated error type serializes it with the correct status code and
body for whichever protocol the service declares.

## Client

```csharp
using Example.Weather;

var client = new WeatherClient(new Uri("https://api.example.com"));
var city = await client.GetCityAsync(new GetCityInput("SEA"));
Console.WriteLine(city.Name);
```

The generated client uses the service's declared protocol by default — no codec
wiring is required.

## Protocol-specific deltas

The handler and client above are identical for `simpleRestJson`, `restJson1`,
`awsJson1_1`, `awsJson1_0`, and `rpcv2Cbor`. The exceptions:

- **Server generation** exists for `simpleRestJson`, `restJson1`, and
  `rpcv2Cbor`. `awsJson1_1`/`awsJson1_0` and `restXml` are **client-only** today,
  so only the client half applies.
- **gRPC** requires transport setup (HTTP/2 Kestrel, `Protocol =
  new GrpcProtocol()`) and is the one protocol where the code differs — see
  [gRPC](/smithy-dotnet/protocols/grpc/). Streaming is also gRPC-specific.

See [Protocol Status](/smithy-dotnet/protocols/status/) for what is generated per
protocol.
