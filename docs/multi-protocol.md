# One Service, Two Transports

This guide shows how to expose one Smithy service over both HTTP and gRPC while
keeping a single service implementation.

The important architectural boundary in NSmithy is:

- one modeled service
- one generated handler contract
- one handler implementation
- multiple generated transport adapters

In practice, that means your application code implements the generated handler
once, and the generated HTTP and gRPC surfaces both call into that same
implementation.

## Model

The service carries both `alloy#simpleRestJson` and `alloy.proto#grpc`:

```smithy
$version: "2"

namespace example.hello

use alloy#simpleRestJson
use alloy.proto#grpc
use alloy.proto#protoIndex

@simpleRestJson
@grpc
service HelloService {
    version: "2026-04-21"
    operations: [SayHello, Ping]
}

@http(method: "GET", uri: "/hello/{name}")
operation SayHello {
    input := {
        @required
        @protoIndex(1)
        @httpLabel
        name: String
    }
}
```

In the current preview:

- `alloy#simpleRestJson` generates the HTTP client/server surfaces
- `alloy.proto#grpc` generates `.proto` files that flow into `Grpc.Tools`
- gRPC-exposed members currently need explicit `alloy.proto#protoIndex` traits
- both transports still share the same Smithy-generated shapes, descriptors,
  and handler interfaces

## Server

The generated server surface gives you two explicit transport mappers:

- `MapHelloServiceHttp()`
- `MapHelloServiceGrpc()`

Your application registers one handler and maps both transports:

```csharp
using Example.Hello;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5000, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1;
    });
    options.ListenLocalhost(5001, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
    });
});
builder.Services.AddGrpc();
builder.Services.AddHelloServiceHandler<HelloHandler>();

var app = builder.Build();
app.MapHelloServiceHttp();
app.MapHelloServiceGrpc();
app.Run();

internal sealed class HelloHandler : IHelloServiceHandler
{
    public Task<SayHelloOutput> SayHelloAsync(
        SayHelloInput input,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new SayHelloOutput("server", $"hello, {input.Name}"));
    }

    public Task<PingOutput> PingAsync(
        PingInput input,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PingOutput("server", $"pong, {input.Name}"));
    }
}
```

Why two ports? In the cleartext local-development setup, REST uses HTTP/1.1 and
gRPC uses HTTP/2. Without TLS/ALPN, sharing a single endpoint is unreliable, so
the example uses:

- HTTP on `http://localhost:5000`
- gRPC on `http://localhost:5001`

## Clients

The same modeled service can also produce two client surfaces:

- a generated Smithy HTTP client
- a generated Smithy-shaped gRPC adapter over the emitted `.proto`

Example:

```csharp
using Example.Hello;
using Grpc.Net.Client;

IHelloServiceClient httpClient = new HelloServiceClient(
    new HttpClient(),
    new SmithyClientOptions { Endpoint = new Uri("http://localhost:5000") });

var httpHello = await httpClient.SayHelloAsync(new SayHelloInput("world"));

using var channel = GrpcChannel.ForAddress("http://localhost:5001");
IHelloServiceClient grpcClient = new HelloServiceGrpcClient(channel);
var grpcHello = await grpcClient.SayHelloAsync(new SayHelloInput("world"));
```

Both clients call the same service semantics through different transports.

## Example

The full working example in this repository lives under `examples/grpc/dotnet`
and shows:

- one Smithy model with both traits
- one handler implementation reused across transports
- generated HTTP and gRPC server mappings
- generated HTTP and gRPC clients

If you are consuming NSmithy from NuGet, the important part is the hosting
shape, not the repository workflow:

- run your ASP.NET Core application with the HTTP and gRPC mappings enabled
- target the HTTP client at `http://localhost:5000`
- target the gRPC client at `http://localhost:5001`

If you want to inspect a working end-to-end sample, see the repository example
at `examples/grpc/dotnet`.

## Current Limits

This is still preview functionality.

- the dual-protocol path depends on the current `alloy#simpleRestJson` and
  `alloy.proto#grpc` integration
- local development should use separate HTTP and gRPC endpoints unless you add
  TLS/ALPN-capable hosting
- client and server projects should only compile the generated surfaces they
  actually need
