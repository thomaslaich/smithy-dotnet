---
title: gRPC
description: alloy.proto#grpc — generate native gRPC client and server surfaces from a Smithy model, with no protoc or Grpc.Tools.
---

`alloy.proto#grpc` generates **native** gRPC client and server surfaces directly
from a Smithy model. NSmithy speaks the gRPC wire contract itself — a schema-driven
protobuf codec (`NSmithy.Codecs.Proto`) plus a gRPC transport binding
(`NSmithy.Protocols.Grpc`) — so there is **no `protoc`, `Grpc.Tools`, or `Grpc.Net`
dependency**. The generated surfaces match the same protocol-agnostic handler and
client interfaces used by the HTTP protocols. Status: **Experimental**.

Because the bytes on the wire are standard protobuf over gRPC/HTTP/2, an NSmithy
peer interoperates with a `Grpc.Net` peer in either direction. A `.proto` file can
still be emitted (see [Generating a `.proto`](#generating-a-proto-for-external-peers))
when you need to build a non-NSmithy peer the conventional way.

See [Protocol Status](/smithy-dotnet/protocols/status/) for current maturity
details.

## Maven Dependency

```json
"com.disneystreaming.alloy:alloy-core:0.3.38"
```

## NuGet Packages

| Purpose | Packages |
| --- | --- |
| gRPC server (ASP.NET Core) | `NSmithy.Server.AspNetCore`, `NSmithy.Protocols.Grpc` |
| gRPC client | `NSmithy.Client`, `NSmithy.Protocols.Grpc` |

`NSmithy.Protocols.Grpc` pulls in `NSmithy.Codecs.Proto` (the protobuf codec)
transitively. No protobuf toolchain is required.

## Modeling

Apply `@grpc` to the service and `@protoIndex` to every member in an operation's
input or output:

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

`@protoIndex` assigns the proto field number. It is currently required on every
member that appears in a proto message — omitting it is a model error.
`@protoNumType` selects integer wire types (`sint`/`uint`/`fixed`/`sfixed`).

## On the Wire

NSmithy speaks standard gRPC — length-prefixed protobuf over HTTP/2,
interoperable with any gRPC peer. Each member's `@protoIndex` is its protobuf
field number.

A unary `GetCity { cityId: "123" }` call posts to
`/{namespace}.{Service}/{Method}`:

```
POST /example.weather.Weather/GetCity HTTP/2
content-type: application/grpc+proto
te: trailers

<gRPC frame><protobuf message>
```

Each message is a gRPC frame — a 1-byte compression flag, a 4-byte big-endian
length, then the protobuf payload. For `cityId: "123"`:

```
00              compression flag (0 = uncompressed)
00 00 00 05     message length = 5 bytes (big-endian)
0a              field 1, wire type 2 (LEN)   ← cityId, @protoIndex(1)
03              string length = 3
31 32 33        "123"
```

The response returns the output in the same framing and signals the result with
the `grpc-status` trailer (`0` = OK). Modeled errors return HTTP 200 with a
non-zero `grpc-status`; NSmithy carries the Smithy error shape id in the
`grpc-message` / `x-smithy-grpc-error` trailer for typed dispatch.

## Server

gRPC is the one protocol where the hosting and client code differs from the
[shared usage example](/smithy-dotnet/protocols/usage/): it needs HTTP/2
transport and a gRPC-specific client protocol. The generated handler interface
itself works the same way — you implement one method per operation.

Configure Kestrel to serve HTTP/2 on a dedicated port. Cleartext gRPC requires
HTTP/2; mixing HTTP/1.1 REST and cleartext gRPC on the same port is unreliable
without TLS/ALPN. There is no `AddGrpc()` call — the generated `MapWeatherServiceGrpc`
maps the gRPC method routes itself:

```csharp
using Example.Weather;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5001, o => o.Protocols = HttpProtocols.Http2);
});
builder.Services.AddWeatherServiceHandler<WeatherHandler>();

var app = builder.Build();
app.MapWeatherServiceGrpc();
app.Run();

internal sealed class WeatherHandler : IWeatherServiceHandler
{
    public Task<GetCityOutput> GetCityAsync(
        GetCityInput input, CancellationToken ct = default) =>
        Task.FromResult(new GetCityOutput("Seattle"));
}
```

## Client

The generated `WeatherClient` is a native NSmithy client over an HTTP/2
`HttpClient` — no `GrpcChannel`. Pass `GrpcProtocol` to select gRPC; the client
configures the HTTP/2 `HttpClient` for you:

```csharp
using Example.Weather;
using NSmithy.Protocols.Grpc;

var client = new WeatherClient(
    new Uri("http://localhost:5001"),
    new() { Protocol = new GrpcProtocol() });

var city = await client.GetCityAsync(new GetCityInput("SEA"));
Console.WriteLine(city.Name); // Seattle
```

For a service that also declares an HTTP protocol (e.g. `@simpleRestJson` +
`@grpc`), the same client speaks either — set `Protocol = new GrpcProtocol()` for
gRPC, or leave `Protocol` unset for the default (primary) protocol. To reuse a
pre-configured `HttpClient`, pass it as the first argument (it must be HTTP/2 for
gRPC). See [Client Configuration](/smithy-dotnet/guides/client-configuration/).

## Streaming

Native gRPC supports event streaming operations whose streaming member targets an
event union. Generated clients and handlers use `IAsyncEnumerable<TEvent>`:

- server streaming returns `IAsyncEnumerable<TEvent>`
- client streaming accepts `IAsyncEnumerable<TEvent>`
- bidirectional streaming accepts and returns `IAsyncEnumerable<TEvent>`

Model a streaming operation by targeting a `@streaming` union. Each event member
carries a `@protoIndex`, the same as any other gRPC member:

```smithy
@streaming
union ChatEvent {
    @protoIndex(1)
    message: MessageEvent
}

/// Server-streaming: one request, many events.
operation WatchRoom {
    input := {
        @required
        @protoIndex(1)
        room: String
    }
    output := {
        @protoIndex(1)
        events: ChatEvent
    }
}
```

The handler returns an `IAsyncEnumerable<ChatEvent>` and yields events as they
occur:

```csharp
public async IAsyncEnumerable<ChatEvent> WatchRoomAsync(
    WatchRoomInput input,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    for (var i = 1; i <= 3; i++)
    {
        await Task.Delay(25, ct);
        yield return ChatEvent.FromMessage(
            new MessageEvent(User: "server", Text: $"{input.Room}: update {i}"));
    }
}
```

The client consumes the stream with `await foreach`:

```csharp
await foreach (var evt in client.WatchRoomAsync(new WatchRoomInput("general"), ct))
{
    if (evt is ChatEvent.Message m)
        Console.WriteLine($"{m.Value.User}: {m.Value.Text}");
}
```

Client-streaming and bidirectional operations follow the same shape — the
streaming member becomes an `IAsyncEnumerable<TEvent>` parameter, a return
value, or both. See the runnable
[`examples/grpc-streaming`](https://github.com/thomaslaich/smithy-dotnet/tree/main/examples/grpc-streaming)
project for all three.

Streaming payload blobs are not implemented yet. The streaming support here is
for event streams, matching the common gRPC shape.

## Generating a `.proto` for external peers

Setting `SmithyGrpc` (or running `smithy-proto-codegen`) still emits a `.proto`
from the model. Feed it to `protoc`/`Grpc.Tools` to build a `Grpc.Net` peer when
you need to interoperate with a non-NSmithy client or server — the native NSmithy
surfaces speak the same wire format.

## Current Limitations

- `@protoIndex` is required on every input and output member.
- Streaming support is event-stream oriented and still early; streaming payload
  blobs, stream errors, and cancellation behavior need more coverage.
- The full unary surface — scalars and `@protoNumType`, lists/maps, `@sparse`
  maps, string and int enums, unions and `@protoInlinedOneOf`, `Timestamp`, and
  `Document` — is supported.
- Cleartext development requires separate HTTP/1.1 and HTTP/2 ports.
- Smallest conformance test surface of any supported protocol.
