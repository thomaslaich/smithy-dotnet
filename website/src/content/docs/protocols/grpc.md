---
title: gRPC
description: Generate native gRPC clients and servers from alloy.proto#grpc without protoc or Grpc.Tools.
---

`alloy.proto#grpc` generates native gRPC clients and ASP.NET Core servers from a
Smithy model. NSmithy provides its own protobuf codec and gRPC transport, so an
NSmithy service does not need `protoc`, `Grpc.Tools`, `Google.Protobuf`, or
`Grpc.Net`.

The wire contract is standard protobuf over gRPC and HTTP/2. NSmithy peers can
interoperate with conventional gRPC implementations through a generated
`.proto` file.

See [Protocol Status](../status/) for maturity and test coverage.

## Protocol behavior

| Area | gRPC |
| --- | --- |
| Route | `/{namespace}.{Service}/{Operation}` |
| Body | Protobuf |
| Framing | Standard gRPC message frames over HTTP/2 |
| Errors | `grpc-status` plus Smithy error metadata |
| Streaming | Server, client, and bidirectional event streams |
| Smithy requirement | `@protoIndex` on protobuf fields |

## Modeling

Apply `@grpc` to the service and give each protobuf field a stable
`@protoIndex`:

```smithy
$version: "2"

namespace example.weather

use alloy.proto#grpc
use alloy.proto#protoIndex

@grpc
service Weather {
    version: "2026-01-01"
    operations: [GetCity]
}

operation GetCity {
    input := {
        @required
        @protoIndex(1)
        cityId: String
    }
    output := {
        @required
        @protoIndex(1)
        name: String
    }
}
```

`@protoIndex` is the protobuf field number and is part of the stable wire
contract. Omitting it from an input or output member is a model error.
`@protoNumType` selects integer encodings such as `sint32`, `uint64`, or
`fixed32`.

## Server

gRPC requires HTTP/2. Configure a dedicated cleartext HTTP/2 port for local
development, then map the generated service:

```csharp
using Example.Weather;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5001, listener =>
        listener.Protocols = HttpProtocols.Http2);
});
builder.Services.AddWeatherServiceHandler<WeatherHandler>();

var app = builder.Build();
app.MapWeatherService();
app.Run();
```

There is no `AddGrpc()` call. The generated mapping registers the gRPC method
routes directly. Cleartext REST and gRPC should use separate ports unless TLS
and ALPN negotiate the HTTP version.

## Client

The generated client uses an HTTP/2 `HttpClient`. Select `GrpcProtocol` in the
client configuration:

```csharp
using Example.Weather;
using NSmithy.Protocols.Grpc;

var client = new WeatherClient(
    new Uri("http://localhost:5001"),
    new() { Protocol = new GrpcProtocol() });

var city = await client.GetCityAsync(new GetCityInput("SEA"));
```

The endpoint constructor and generated dependency injection helper request exact
HTTP/2 automatically. A caller-owned `HttpClient` must set
`DefaultRequestVersion` to HTTP/2 and `DefaultVersionPolicy` to
`RequestVersionExact`.

## Streaming

Model an event stream with an `@streaming` union:

```smithy
@streaming
union ChatEvent {
    @protoIndex(1)
    message: MessageEvent
}

operation WatchRoom {
    input := {
        @required
        @protoIndex(1)
        room: String
    }
    output := {
        events: ChatEvent
    }
}
```

Generated input and output shapes expose streaming members as
`IAsyncEnumerable<TEvent>`. An output stream provides server streaming, an
input stream provides client streaming, and streams on both sides provide
bidirectional streaming.

Streaming blob payloads are separate from gRPC event streaming and are not
implemented.

## Interoperability and `.proto` generation

NSmithy can emit a `.proto` file from the Smithy model through `SmithyGrpc` or
`smithy-proto-codegen`. Use that file with `protoc` or `Grpc.Tools` when the
other peer uses a conventional gRPC stack. The Smithy model remains the source
of truth.

The supported protobuf surface includes scalars, `@protoNumType`, lists, maps,
`@sparse`, string and integer enums, unions, `@protoInlinedOneOf`,
`Timestamp`, and `Document`.

## Dependencies

Add the Alloy model package to `smithy-build.json`:

```json
"com.disneystreaming.alloy:alloy-core:0.3.38"
```

| Surface | Packages |
| --- | --- |
| Client | `NSmithy.Client`, `NSmithy.Protocols.Grpc` |
| Server | `NSmithy.Server.AspNetCore`, `NSmithy.Protocols.Grpc` |

`NSmithy.Protocols.Grpc` includes the NSmithy protobuf codec transitively.

## Examples

- [Unary gRPC library service](https://github.com/thomaslaich/smithy-dotnet/tree/main/examples/grpc)
- [Streaming and Grpc.Net interoperability](https://github.com/thomaslaich/smithy-dotnet/tree/main/examples/grpc-streaming)

## Protocol source

- [Alloy repository](https://github.com/disneystreaming/alloy)
- [Native gRPC design](https://github.com/thomaslaich/smithy-dotnet/blob/main/designs/native-grpc.md)
