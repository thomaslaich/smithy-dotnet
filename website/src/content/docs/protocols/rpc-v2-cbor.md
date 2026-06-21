---
title: RPC v2 CBOR
description: smithy.protocols#rpcv2Cbor — binary CBOR encoding over HTTP. Client and server support.
---

`smithy.protocols#rpcv2Cbor` is Smithy's binary protocol. Messages are encoded
as [CBOR](https://cbor.io/) and carried over HTTP POST requests on a fixed
path derived from the service and operation names. Status: **Preview**.

## Maven Dependency

No extra Maven dependency beyond the codegen plugin — `smithy.protocols` shapes
are bundled with the Smithy CLI.

## NuGet Package

```xml
<PackageReference Include="NSmithy.Codecs.Cbor" Version="0.2.0" />
```

## Modeling

Apply `@rpcv2Cbor` to the service. Operations do not carry `@http` traits —
the protocol maps each operation to a fixed
`POST /service/{Service}/operation/{Operation}` path automatically:

```smithy
$version: "2"

namespace example.hello

use smithy.protocols#rpcv2Cbor

@rpcv2Cbor
service HelloService {
    version: "2026-01-01"
    operations: [SayHello]
}

operation SayHello {
    input := {
        @required
        name: String
    }
    output := {
        @required
        message: String
    }
    errors: [InvalidName]
}

@error("client")
structure InvalidName {
    message: String
}
```

## NuGet Packages

### Server

```xml
<PackageReference Include="NSmithy.Server.AspNetCore" Version="0.2.0" />
```

### Client

```xml
<PackageReference Include="NSmithy.Client" Version="0.2.0" />
<PackageReference Include="NSmithy.Codecs.Cbor" Version="0.2.0" />
<PackageReference Include="NSmithy.Protocols.RpcV2Cbor" Version="0.2.0" />
```

## Server

Add the generated server handler to your ASP.NET Core app. The protocol path
(`POST /service/{Service}/operation/{Operation}`) is mapped automatically:

```csharp
using Example.Hello;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHelloServiceHandler<MyHelloHandler>();

var app = builder.Build();
app.MapHelloServiceHttp();
app.Run();

internal sealed class MyHelloHandler : IHelloServiceHandler
{
    public Task<SayHelloOutput> SayHelloAsync(
        SayHelloInput input,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new SayHelloOutput("my-server", $"Hello, {input.Name}!"));
}
```

## Client

The CBOR codec is wired up automatically by the generated client — no manual
configuration is required:

```csharp
using Example.Hello;

var client = new HelloServiceClient(new Uri("https://api.example.com"));

try
{
    var response = await client.SayHelloAsync(new SayHelloInput("world"));
    Console.WriteLine(response.Message);
}
catch (InvalidName ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}
```

## Example

A complete working server+client example is available in
[`examples/rpcv2cbor`](https://github.com/thomaslaich/smithy-dotnet/tree/main/examples/rpcv2cbor).
