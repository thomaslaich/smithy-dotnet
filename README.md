_Work in Progress: NSmithy is preview-stage software. Public APIs, generated
code shape, package boundaries, and code generation behavior may change. The
current C# code generator is implemented in this repository so the runtime,
generation model, and generated surface can evolve together; once the design is
more stable, the code generation layer will likely move to a Java-based Smithy
plugin to align more closely with the broader Smithy ecosystem._

# NSmithy

NSmithy is a preview-stage .NET toolkit that turns a [Smithy](https://smithy.io)
model into idiomatic C# at build time. From a single contract you get the same
model types, typed clients, and server scaffolding that any other Smithy
language would produce — driven from MSBuild, with no hand-written protocol
glue.

What you get out of the model:

- **Code generation from MSBuild.** `NSmithy.MSBuild` invokes the Smithy CLI
  and the C# code generator during `dotnet build`. Generated files land under
  `obj/.../Smithy/` and are compiled into your project automatically.
- **C# model types.** Records for structures, discriminated unions for unions,
  string- and int-enum types, and runtime `Document` values for open content —
  all annotated with Smithy shape metadata so they round-trip through the
  serializers.
- **Typed protocol-aware clients.** Generated `I{Service}Client` interface and
  implementation for `alloy#simpleRestJson`, `aws.protocols#restJson1`,
  `aws.protocols#restXml`, and `smithy.protocols#rpcv2Cbor`, including HTTP
  binding (path, query, headers, payload), error deserialization, and a
  middleware pipeline for retries and customization.
- **ASP.NET Core server surfaces.** For `alloy#simpleRestJson` services,
  generated endpoint mapping (`MapXxxHttp`) plus per-operation and aggregate
  handler interfaces you implement against typed inputs and outputs.
- **JSON, XML, and CBOR payload codecs.** `NSmithy.Codecs.{Json,Xml,Cbor}`
  serialize and deserialize payloads using Smithy member metadata
  (`@jsonName`, `@xmlName`, sparse maps, default values, timestamp formats,
  etc.).
- **Conformance against official protocol tests.** Generated clients are
  exercised against the upstream Smithy/AWS and alloy `httpRequestTests` /
  `httpResponseTests` fixtures (see [tests/Conformance](https://github.com/thomaslaich/smithy-dotnet/tree/main/tests/Conformance)),
  so coverage is measured against the same suite that other Smithy
  implementations use.

Additional protocols, broader protocol compliance, and NativeAOT serializer
generation are planned. See the
[roadmap](https://github.com/thomaslaich/smithy-dotnet/blob/main/docs/planning/roadmap.md)
for details.

Generated-client conformance against the official Smithy/AWS and
[alloy](https://github.com/disneystreaming/alloy) protocol test suites is
exercised per protocol under [tests/Conformance](https://github.com/thomaslaich/smithy-dotnet/tree/main/tests/Conformance).
Each suite emits its current pass rate as part of `dotnet test` output.

## Install

For generated clients, add:

```xml
<ItemGroup>
  <PackageReference Include="NSmithy.Client" Version="0.1.0-preview.4" />
  <PackageReference Include="NSmithy.Core" Version="0.1.0-preview.4" />
  <PackageReference Include="NSmithy.Http" Version="0.1.0-preview.4" />
  <PackageReference Include="NSmithy.Codecs.Json" Version="0.1.0-preview.4" />
  <PackageReference Include="NSmithy.MSBuild" Version="0.1.0-preview.4" PrivateAssets="all" />
</ItemGroup>
```

For generated ASP.NET Core `simpleRestJson` servers, also add:

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
  <PackageReference Include="NSmithy.Server" Version="0.1.0-preview.4" />
  <PackageReference Include="NSmithy.Server.AspNetCore" Version="0.1.0-preview.4" />
</ItemGroup>
```

## Add a Model

Add a `smithy-build.json` at the project root and a model file. Minimal
`alloy#simpleRestJson` example:

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

Build the project. Generated files appear under `obj/<configuration>/<tfm>/Smithy/`.

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

For smaller services, implement the aggregate service handler interface:

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

Service definitions tend to fragment over time. Teams publish different API
descriptions, generate clients differently, adopt different protocols, and
couple contracts to specific frameworks or transports. The result is usually a
mix of handwritten clients, drifting conventions, and contracts that are hard
to reuse across stacks.

Smithy separates the service contract from the implementation. You define the
model once, distribute it like any other package, and generate client and
server surfaces across languages without locking the contract to one transport
stack.

`gRPC` solves some of the same problems, but within a single protocol stack.
Smithy works at a higher level: one model can target multiple protocols and be
extended with custom traits and protocols when needed.

That matters when you want:

- a stable contract that is not tied to one framework or HTTP stack
- room to evolve protocols and implementations without redefining the service
- consistent client, server, and documentation surfaces across languages
- less hand-written protocol glue repeated in every application

## Why NSmithy?

There is no official Smithy implementation for .NET today. NSmithy fills that
gap by making Smithy feel native in the .NET ecosystem while supporting
[alloy](https://github.com/disneystreaming/alloy) traits and workflows that
matter in practice.

In practice, that means contract-first, protocol-aware generation for .NET with
generated C# types, typed clients, and ASP.NET Core server surfaces.


## Smithy CLI

`NSmithy.MSBuild` invokes the Smithy CLI during `dotnet build`. For some .NET
teams, the Java requirement around the Smithy toolchain is an adoption blocker,
especially when the rest of the stack is otherwise entirely .NET.

The easiest way to contain that is to use a project-local environment that
installs both the Smithy CLI and Java together instead of relying on machine-
level setup. The recommended option here is
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
