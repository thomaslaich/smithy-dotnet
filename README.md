_Work in Progress: NSmithy is preview-stage software. Public APIs, generated
code shape, package boundaries, and code generation behavior may change. The
current C# code generator is implemented in this repository so the runtime,
generation model, and generated surface can evolve together; once the design is
more stable, the code generation layer will likely move to a Java-based Smithy
plugin to align more closely with the broader Smithy ecosystem._

# NSmithy

NSmithy is a preview-stage .NET toolkit for generating C# models, typed HTTP
clients, and ASP.NET Core server surfaces from [Smithy](https://smithy.io) models.

- Run the Smithy CLI from MSBuild
- Generate C# model types, typed `restJson1` and `simpleRestJson` clients, and
  `simpleRestJson` ASP.NET Core server endpoints and handler interfaces
- Serialize and deserialize JSON payloads through Smithy metadata

Additional protocols, broader protocol compliance, and NativeAOT serializer
generation are planned. See the
[roadmap](https://github.com/thomaslaich/smithy-dotnet/blob/main/docs/planning/roadmap.md)
for details.

NSmithy tracks generated-client conformance against official Smithy/AWS and
[alloy](https://github.com/disneystreaming/alloy) protocol test suites in
[docs/generated/protocol-conformance.md](https://github.com/thomaslaich/smithy-dotnet/blob/main/docs/generated/protocol-conformance.md).

## Install

For generated clients, add:

```xml
<ItemGroup>
  <PackageReference Include="NSmithy.Client" Version="0.1.0-preview.3" />
  <PackageReference Include="NSmithy.Core" Version="0.1.0-preview.3" />
  <PackageReference Include="NSmithy.Http" Version="0.1.0-preview.3" />
  <PackageReference Include="NSmithy.Codecs.Json" Version="0.1.0-preview.3" />
  <PackageReference Include="NSmithy.MSBuild" Version="0.1.0-preview.3" PrivateAssets="all" />
</ItemGroup>
```

For generated ASP.NET Core `simpleRestJson` servers, also add:

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
  <PackageReference Include="NSmithy.Server" Version="0.1.0-preview.3" />
  <PackageReference Include="NSmithy.Server.AspNetCore" Version="0.1.0-preview.3" />
</ItemGroup>
```

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
using NSmithy.Client;

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

## Why Smithy?

In a large company, service definitions often become fragmented across teams,
tools, and protocols. Different teams publish different styles of API descriptions,
clients may be generated from hosted OpenAPI contracts or shipped as separate packages
in specific languages, protocol choices vary across systems, and the organization becomes more
polyglot over time. It is common to end up with a mix of handwritten clients,
OpenAPI documents hosted somewhere, internal conventions that drift by team,
and service definitions that are tightly coupled to one framework or transport.

Smithy gives you a cleaner separation between service definition and service
implementation. You define the contract once at the model layer, distribute it
through normal package-manager workflows, and generate client and server
surfaces across languages without tying the contract to one transport stack.

`gRPC` can solve some of these problems well, but it does so within a single
protocol stack. Smithy works at a higher level: the model is not tied to one
wire protocol, can target multiple protocols from the same contract, and can be
extended with custom traits and protocols when the built-in ones are not
enough. That flexibility is one of the reasons Smithy remains useful even when
an organization is not standardizing on just one transport model.

That matters when you want:

- a stable contract that is not tied to one framework or HTTP stack
- room to evolve protocols and implementations without redefining the service
- consistent client, server, and documentation surfaces across languages
- less hand-written protocol glue repeated in every application

## Why NSmithy?

NSmithy takes that one step further for .NET. It aims to make Smithy feel
native in the .NET ecosystem while also supporting [alloy](https://github.com/disneystreaming/alloy)
traits and workflows that matter for practical service development beyond the
core Smithy baseline.

In practice, that means bringing contract-first, protocol-aware generation to
.NET with generated C# types, typed clients, ASP.NET Core server surfaces.


## Smithy CLI

`NSmithy.MSBuild` invokes the Smithy CLI during `dotnet build`. For some .NET
teams, the Java requirement around the Smithy toolchain is an adoption blocker,
especially when the rest of the stack is otherwise entirely .NET.

The most practical way to smooth that over is to use a managed project
environment that installs both the Smithy CLI and its Java dependency together,
so the toolchain is local to the project instead of becoming machine-level
setup. The recommended option here is
[pixi](https://pixi.sh) with `smithy-cli` from conda-forge:

```bash
pixi init
pixi add smithy openjdk dotnet
pixi shell
dotnet build
```

When the environment is active, `smithy` is resolved from `PATH`. Set
`SmithyCliPath` to force a specific executable when needed.


## Documentation

- [Protocol Status](https://github.com/thomaslaich/smithy-dotnet/blob/main/docs/protocols/README.md)
- [Quick Start](https://github.com/thomaslaich/smithy-dotnet/blob/main/docs/quick-start.md)
- [Multi-Protocol Guide](https://github.com/thomaslaich/smithy-dotnet/blob/main/docs/multi-protocol.md)
- [MSBuild Reference](https://github.com/thomaslaich/smithy-dotnet/blob/main/docs/msbuild.md)
- [Architecture](https://github.com/thomaslaich/smithy-dotnet/blob/main/docs/architecture/hybrid-codegen.md)
- [Known Limitations](https://github.com/thomaslaich/smithy-dotnet/blob/main/docs/known-limitations.md)
- [Roadmap](https://github.com/thomaslaich/smithy-dotnet/blob/main/docs/planning/roadmap.md)
