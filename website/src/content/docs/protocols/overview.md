---
title: Overview
description: How Smithy protocols map to generated .NET surfaces in NSmithy — and why your client and server code never change when the protocol does.
---

A Smithy protocol trait on a service definition controls two things:

- **Wire format** — how requests and responses are serialized (JSON, XML, CBOR,
  Protobuf)
- **HTTP binding** — how operations, inputs, and outputs map to HTTP methods,
  URIs, headers, and bodies

NSmithy reads the protocol trait and generates the matching client and server
surfaces.

## Your code does not change with the protocol

The protocol trait never reaches your application code. You implement one
generated handler interface and call one typed client; swapping the protocol
trait on the service swaps the wire format without touching either side. What
changes per protocol is layered on top:

- the **service's protocol trait** (`@simpleRestJson`, `@restJson1`,
  `@rpcv2Cbor`, `@grpc`, …), and
- for HTTP protocols, the **HTTP binding traits** on operations (`@http`,
  `@httpLabel`, …); gRPC uses `@protoIndex` instead.

The protocol pages below cover only what is specific to each protocol — its
trait, modeling rules, and wire format.

### Server

You implement one generated handler interface (`IWeatherServiceHandler`, a method
per operation); the generated ASP.NET Core adapter registers it and maps its
routes. Throwing a generated error type serializes it with the correct status
code and body for whichever protocol the service declares. See
[Servers](/smithy-dotnet/servers/) for the full walkthrough and [Hosting &amp;
Multiple Protocols](/smithy-dotnet/servers/hosting/) for exposing one service
over several protocols at once.

### Client

```csharp
using Example.Weather;

var client = new WeatherClient(new Uri("https://api.example.com"));
var city = await client.GetCityAsync(new GetCityInput("SEA"));
Console.WriteLine(city.Name);
```

The generated client uses the service's declared protocol by default — no codec
wiring is required.

## Supported Protocols

| Protocol | Trait | Generated surfaces | Status |
| --- | --- | --- | --- |
| `alloy#simpleRestJson` | `@simpleRestJson` | .NET client, ASP.NET Core server | Preview |
| `aws.protocols#restJson1` | `@restJson1` | .NET client, ASP.NET Core server | Preview |
| `aws.protocols#awsJson1_1` | `@awsJson1_1` | .NET client | Early preview |
| `aws.protocols#awsJson1_0` | `@awsJson1_0` | .NET client | Early preview |
| `aws.protocols#awsQuery` | `@awsQuery` | .NET client | Preview |
| `aws.protocols#ec2Query` | `@ec2Query` | .NET client | Preview |
| `aws.protocols#restXml` | `@restXml` | .NET client | Early preview |
| `smithy.protocols#rpcv2Cbor` | `@rpcv2Cbor` | .NET client, ASP.NET Core server | Preview |
| `alloy.proto#grpc` | `@grpc` | gRPC client, ASP.NET Core gRPC server | Experimental |

See [Protocol Status](/smithy-dotnet/protocols/status/) for conformance numbers,
maturity details, and guidance on which protocol to choose.
