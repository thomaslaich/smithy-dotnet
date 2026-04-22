# Smithy.NET

Smithy.NET is a preview-stage .NET toolkit for generating C# models, typed HTTP
clients, and ASP.NET Core server surfaces from [Smithy](https://smithy.io) models.

- Run the Smithy CLI from MSBuild
- Generate C# model types, typed `restJson1` and `simpleRestJson` clients, and
  `simpleRestJson` ASP.NET Core server endpoints and handler interfaces
- Serialize and deserialize JSON payloads through Smithy metadata

Additional protocols, broader protocol compliance, and NativeAOT serializer
generation are planned. See the
[roadmap](https://github.com/thomaslaich/smithy-dotnet/blob/main/docs/planning/roadmap.md)
for details.

Smithy.NET tracks generated-client conformance against official Smithy/AWS and
Alloy protocol test suites in
[docs/generated/protocol-conformance.md](https://github.com/thomaslaich/smithy-dotnet/blob/main/docs/generated/protocol-conformance.md).

## Packages

| Package | Purpose |
| --- | --- |
| `SmithyNet.Core` | Shared runtime primitives, generated-code attributes, Smithy IDs, and document values. |
| `SmithyNet.CodeGeneration` | Smithy JSON AST reader and C# generator. |
| `SmithyNet.MSBuild` | MSBuild integration that invokes Smithy build and adds generated C# to compilation. |
| `SmithyNet.Json` | Reflection-based JSON serializer for generated Smithy shapes. |
| `SmithyNet.Http` | HTTP transport abstractions and `HttpClient` transport. |
| `SmithyNet.Client` | Operation invoker, client middleware pipeline, errors, and retry middleware. |
| `SmithyNet.Server` | Server dispatch primitives for generated service handlers and middleware. |
| `SmithyNet.Server.AspNetCore` | ASP.NET Core integration package for generated server endpoints. |

## Install

For generated clients, add:

```xml
<ItemGroup>
  <PackageReference Include="SmithyNet.Client" Version="0.1.0-preview.1" />
  <PackageReference Include="SmithyNet.Core" Version="0.1.0-preview.1" />
  <PackageReference Include="SmithyNet.Http" Version="0.1.0-preview.1" />
  <PackageReference Include="SmithyNet.Json" Version="0.1.0-preview.1" />
  <PackageReference Include="SmithyNet.MSBuild" Version="0.1.0-preview.1" PrivateAssets="all" />
</ItemGroup>
```

For generated ASP.NET Core `simpleRestJson` servers, also add:

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
  <PackageReference Include="SmithyNet.Server" Version="0.1.0-preview.1" />
  <PackageReference Include="SmithyNet.Server.AspNetCore" Version="0.1.0-preview.1" />
</ItemGroup>
```

## Smithy CLI

`SmithyNet.MSBuild` invokes the Smithy CLI during `dotnet build`. The recommended
setup is a managed project environment such as
[pixi](https://pixi.sh) with `smithy-cli` from conda-forge:

```bash
pixi init
pixi add smithy
pixi shell
dotnet build
```

When the environment is active, `smithy` is resolved from `PATH`. Set
`SmithyCliPath` to force a specific executable when needed.

## Add a Model

Add a `smithy-build.json` at the project root and a model file. Here is a
minimal `alloy#simpleRestJson` service:

```json
{
  "version": "1.0",
  "sources": ["model"],
  "maven": {
    "dependencies": ["com.disneystreaming.alloy:alloy-core:0.3.38"]
  }
}
```

```smithy
$version: "2"

namespace example.hello

use alloy#simpleRestJson

@simpleRestJson
service HelloService {
    version: "2024-01-01"
    operations: [SayHello]
}

@http(method: "GET", uri: "/hello/{name}")
operation SayHello {
    input := {
        @required
        @httpLabel
        name: String
    }

    output := {
        @required
        message: String
    }
}
```

Set `SmithyGeneratedNamespaces` to limit generated C# to your service namespace
when build dependencies include trait models that should not become C# types:

```xml
<PropertyGroup>
  <SmithyGeneratedNamespaces>example.hello</SmithyGeneratedNamespaces>
</PropertyGroup>
```

Build the project. Generated files appear under
`obj/<configuration>/<tfm>/Smithy/`.

## Generated Client

```csharp
using Example.Hello;
using SmithyNet.Client;

var client = new HelloServiceClient(
    new HttpClient(),
    new SmithyClientOptions { Endpoint = new Uri("http://localhost:8082") }
);

var output = await client.SayHelloAsync(new SayHelloInput("world"));
Console.WriteLine(output.Message);
```

## Generated Server

The compact path implements the aggregate service handler interface:

```csharp
using Example.Hello;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHelloServiceHandler<HelloHandler>();

var app = builder.Build();
app.MapHelloServiceHttp();
app.Run();

internal sealed class HelloHandler : IHelloServiceHandler
{
    public Task<SayHelloOutput> SayHelloAsync(
        SayHelloInput input,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new SayHelloOutput($"hello, {input.Name}"));
}
```

For larger services, register operation handlers individually:

```csharp
builder.Services.AddSingleton<ISayHelloHandler, SayHelloHandler>();
```

## Documentation

- [Protocol Status](https://github.com/thomaslaich/smithy-dotnet/blob/main/docs/protocols/README.md)
- [Quick Start](https://github.com/thomaslaich/smithy-dotnet/blob/main/docs/quick-start.md)
- [Multi-Protocol Guide](https://github.com/thomaslaich/smithy-dotnet/blob/main/docs/multi-protocol.md)
- [MSBuild Reference](https://github.com/thomaslaich/smithy-dotnet/blob/main/docs/msbuild.md)
- [Supported Surface](https://github.com/thomaslaich/smithy-dotnet/blob/main/docs/supported-surface.md)
- [Known Limitations](https://github.com/thomaslaich/smithy-dotnet/blob/main/docs/known-limitations.md)
- [Roadmap](https://github.com/thomaslaich/smithy-dotnet/blob/main/docs/planning/roadmap.md)
