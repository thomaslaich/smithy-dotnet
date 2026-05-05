---
title: gRPC
description: Generate a gRPC client and ASP.NET Core server from a Smithy model.
---

NSmithy can generate gRPC surfaces from a Smithy model annotated with
`alloy.proto#grpc`. The generator emits a `.proto` file that flows into
`Grpc.Tools`, which in turn generates the gRPC stub types. NSmithy then wraps
those stubs with a typed adapter that matches the same handler interface used by
the HTTP surface.

This is experimental in the current preview — see [Protocol Status](../protocols/)
for the current maturity level.

## Model

Add `@grpc` to the service and `@protoIndex` to every member that appears in a
proto message:

```smithy
$version: "2"

namespace example.hello

use alloy#simpleRestJson
use alloy.proto#grpc
use alloy.proto#protoIndex

@grpc
service HelloService {
    version: "2026-01-01"
    operations: [SayHello]
}

@readonly
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

`@protoIndex` assigns the proto field number for each member. It is currently
required on every member that appears in a proto message.

## smithy-build.json

No extra plugin configuration is needed beyond referencing `alloy-core`:

```json
{
  "version": "1.0",
  "sources": ["model"],
  "maven": {
    "dependencies": [
      "com.disneystreaming.alloy:alloy-core:0.3.38",
      "io.github.thomaslaich.nsmithy:smithy-csharp-codegen:0.1.0-preview.5"
    ]
  },
  "plugins": {
    "csharp-codegen": {
      "service": "example.hello#HelloService",
      "baseNamespace": ""
    }
  }
}
```

## Project Setup

Add `Grpc.AspNetCore` (server) or `Grpc.Net.Client` (client) alongside the
standard NSmithy packages:

```xml
<ItemGroup>
  <PackageReference Include="NSmithy.MSBuild" Version="0.1.0-preview.5" PrivateAssets="all" />
  <PackageReference Include="NSmithy.Core" Version="0.1.0-preview.5" />
  <PackageReference Include="NSmithy.Http" Version="0.1.0-preview.5" />
  <PackageReference Include="NSmithy.Client" Version="0.1.0-preview.5" />
  <PackageReference Include="NSmithy.Codecs.Json" Version="0.1.0-preview.5" />
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
  <PackageReference Include="NSmithy.Server" Version="0.1.0-preview.5" />
  <PackageReference Include="NSmithy.Server.AspNetCore" Version="0.1.0-preview.5" />

  <!-- gRPC -->
  <PackageReference Include="Grpc.AspNetCore" Version="2.67.0" />
</ItemGroup>
```

`Grpc.Tools` is bundled with `Grpc.AspNetCore` and picks up the generated
`.proto` file automatically.

## Server

The generated server surface exposes `MapHelloServiceGrpc()` alongside the HTTP
mapper. Configure Kestrel to serve HTTP/1.1 and HTTP/2 on separate ports when
running without TLS, since cleartext gRPC requires HTTP/2 and REST typically
uses HTTP/1.1:

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
app.MapHelloServiceGrpc();  // gRPC on :5001
app.Run();

internal sealed class HelloHandler : IHelloServiceHandler
{
    public Task<SayHelloOutput> SayHelloAsync(
        SayHelloInput input,
        CancellationToken ct = default) =>
        Task.FromResult(new SayHelloOutput($"Hello, {input.Name}!"));
}
```

## gRPC Client

The generated gRPC client wraps the Grpc.Tools-generated stub:

```csharp
using Example.Hello;
using Grpc.Net.Client;

using var channel = GrpcChannel.ForAddress("http://localhost:5001");
var client = new HelloServiceGrpcClient(channel);

var response = await client.SayHelloAsync(new SayHelloInput("world"));
Console.WriteLine(response.Message); // Hello, world!
```

## Related

- [Multi-Protocol](./multi-protocol/) — serve both HTTP and gRPC from one handler
- [Protocol Status](../protocols/) — current gRPC maturity level
