---
title: gRPC
description: alloy.proto#grpc — emit .proto files and generate gRPC client and server adapters from a Smithy model.
---

`alloy.proto#grpc` generates a `.proto` file from a Smithy model. `Grpc.Tools`
picks up that file and emits the low-level gRPC stubs; NSmithy wraps those stubs
with typed client and server adapters that match the same handler interface used
by the HTTP surface. Status: **Experimental**.

## Maven Dependency

```json
"com.disneystreaming.alloy:alloy-core:0.3.38"
```

## NuGet Packages

| Purpose | Package |
| --- | --- |
| gRPC server (ASP.NET Core) | `Grpc.AspNetCore` (bundles `Grpc.Tools`) |
| gRPC client | `Grpc.Net.Client` |

Add the standard NSmithy server or client packages alongside these.

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

## Server

Configure Kestrel to serve HTTP/2 on a dedicated port. Cleartext gRPC requires
HTTP/2; mixing HTTP/1.1 REST and cleartext gRPC on the same port is unreliable
without TLS/ALPN:

```csharp
using Example.Hello;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5001, o => o.Protocols = HttpProtocols.Http2);
});
builder.Services.AddGrpc();
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

```csharp
using Example.Hello;
using Grpc.Net.Client;

using var channel = GrpcChannel.ForAddress("http://localhost:5001");
var client = new HelloServiceGrpcClient(channel);

var response = await client.SayHelloAsync(new SayHelloInput("world"));
Console.WriteLine(response.Message); // Hello, world!
```

## Current Limitations

- `@protoIndex` is required on every input and output member.
- No streaming operations yet.
- Cleartext development requires separate HTTP/1.1 and HTTP/2 ports.
- Smallest conformance test surface of any supported protocol.

## Related

- [Multi-Protocol](/smithy-dotnet/guides/multi-protocol/) — serve one handler over both HTTP and gRPC
- [Protocol Status](/smithy-dotnet/protocols/) — coverage overview
