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

namespace example.hello

use alloy.proto#grpc
use alloy.proto#protoIndex

@grpc
service HelloService {
    version: "2026-01-01"
    operations: [SayHello]
}

operation SayHello {
    input := {
        @required
        @protoIndex(1)
        name: String
    }
    output := {
        @required
        @protoIndex(1)
        message: String
    }
}
```

`@protoIndex` assigns the proto field number. It is currently required on every
member that appears in a proto message — omitting it is a model error.
`@protoNumType` selects integer wire types (`sint`/`uint`/`fixed`/`sfixed`).

## Server

Configure Kestrel to serve HTTP/2 on a dedicated port. Cleartext gRPC requires
HTTP/2; mixing HTTP/1.1 REST and cleartext gRPC on the same port is unreliable
without TLS/ALPN. There is no `AddGrpc()` call — the generated `MapHelloServiceGrpc`
maps the gRPC method routes itself:

```csharp
using Example.Hello;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5001, o => o.Protocols = HttpProtocols.Http2);
});
builder.Services.AddHelloServiceHandler<HelloHandler>();

var app = builder.Build();
app.MapHelloServiceGrpc();
app.Run();

internal sealed class HelloHandler : IHelloServiceHandler
{
    public Task<SayHelloOutput> SayHelloAsync(
        SayHelloInput input, CancellationToken ct = default) =>
        Task.FromResult(new SayHelloOutput($"Hello, {input.Name}!"));
}
```

## Client

The generated `HelloServiceClient` is a native NSmithy client over an HTTP/2
`HttpClient` — no `GrpcChannel`. Pass `GrpcProtocol` to select gRPC; the client
configures the HTTP/2 `HttpClient` for you:

```csharp
using Example.Hello;
using NSmithy.Protocols.Grpc;

var client = new HelloServiceClient(
    new Uri("http://localhost:5001"),
    new() { Protocol = new GrpcProtocol() });

var response = await client.SayHelloAsync(new SayHelloInput("world"));
Console.WriteLine(response.Message); // Hello, world!
```

For a service that also declares an HTTP protocol (e.g. `@simpleRestJson` +
`@grpc`), the same client speaks either — set `Protocol = new GrpcProtocol()` for
gRPC, or leave `Protocol` unset for the default (primary) protocol. To reuse a
pre-configured `HttpClient`, pass it as the first argument (it must be HTTP/2 for
gRPC). See [Client Configuration](/smithy-dotnet/guides/client-configuration/).

## Generating a `.proto` for external peers

Setting `SmithyGrpc` (or running `smithy-proto-codegen`) still emits a `.proto`
from the model. Feed it to `protoc`/`Grpc.Tools` to build a `Grpc.Net` peer when
you need to interoperate with a non-NSmithy client or server — the native NSmithy
surfaces speak the same wire format.

## Current Limitations

- `@protoIndex` is required on every input and output member.
- **No streaming operations yet (unary only).** This is the main remaining gap;
  the full unary surface — scalars and `@protoNumType`, lists/maps, `@sparse`
  maps, string and int enums, unions and `@protoInlinedOneOf`, `Timestamp`, and
  `Document` — is supported.
- Cleartext development requires separate HTTP/1.1 and HTTP/2 ports.
- Smallest conformance test surface of any supported protocol.
