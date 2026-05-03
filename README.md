_Work in Progress: NSmithy is a proof of concept. The current implementation
demonstrates that Smithy models can drive idiomatic C# clients and ASP.NET Core
servers end-to-end, but the protocol implementations are not yet on par with
the [Smithy reference implementations](https://github.com/smithy-lang/smithy)._

# NSmithy

NSmithy is a preview-stage .NET toolkit that turns a [Smithy](https://smithy.io)
model into idiomatic C# at build time. From a single contract you get the same
model types, typed clients, and server scaffolding that any other Smithy
language would produce. NSmithy aims to fully integrate into your MSBuild workflow,
in order to make code generation as seemless as possible.

## Features

- **Code generation from MSBuild**: Generates C# types, clients, and ASP.NET Core server scaffolding from Smithy models during `dotnet build`.
- **Typed protocol-aware clients**: Supports `alloy#simpleRestJson`, `aws.protocols#restJson1`, `aws.protocols#restXml`, and `smithy.protocols#rpcv2Cbor`.
- **ASP.NET Core server surfaces**: Implements Smithy services as ASP.NET Core endpoints with minimal boilerplate.
- **Conformance**: Protocols are tested against the official Smithy/AWS and alloy protocol test suites.

See the [roadmap](https://github.com/thomaslaich/smithy-dotnet/blob/main/docs/planning/roadmap.md) for planned features.

## Quick Start

The fastest way to try NSmithy is with the [smithy-dotnet-minimal-pixi](https://github.com/thomaslaich/smithy-dotnet-minimal-pixi) example. It shows a minimal project using NSmithy and [pixi](https://pixi.sh) for environment management.

---

### Or, set up manually:

1. **Create a new .NET project:**

   ```bash
   dotnet new console -n MySmithyApp
   cd MySmithyApp
   ```

2. **Add NSmithy dependencies:**
   Edit your `.csproj` to include:

   ```xml
   <ItemGroup>
     <PackageReference Include="NSmithy.Client" Version="0.1.0-preview.5" />
     <PackageReference Include="NSmithy.Core" Version="0.1.0-preview.5" />
     <PackageReference Include="NSmithy.Http" Version="0.1.0-preview.5" />
     <PackageReference Include="NSmithy.Codecs.Json" Version="0.1.0-preview.5" />
     <PackageReference Include="NSmithy.MSBuild" Version="0.1.0-preview.5" PrivateAssets="all" />
   </ItemGroup>
   ```

3. **Add a Smithy model and smithy-build.json:**
   - Create a `model/hello.smithy` file:

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

   - Create a `smithy-build.json` at the project root:

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

4. **Build the project:**

   ```bash
   dotnet build
   ```

5. **Use the generated client/server code in your app.**

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

---

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

## Smithy CLI & Environment

The easiest way to get started is with [pixi](https://pixi.sh) and the minimal example linked above. This sets up Smithy CLI, Java, and .NET in a project-local environment:

```bash
pixi init
pixi add smithy openjdk dotnet
pixi shell
dotnet build
```

When the environment is active, `smithy` is resolved from `PATH`. Set `SmithyCliPath` to force a specific executable if needed.

## Documentation

- [Protocol Status](https://github.com/thomaslaich/smithy-dotnet/blob/main/docs/protocols/README.md)
- [Quick Start](https://github.com/thomaslaich/smithy-dotnet/blob/main/docs/quick-start.md)
- [Multi-Protocol Guide](https://github.com/thomaslaich/smithy-dotnet/blob/main/docs/multi-protocol.md)
- [MSBuild Reference](https://github.com/thomaslaich/smithy-dotnet/blob/main/docs/msbuild.md)
- [Architecture](https://github.com/thomaslaich/smithy-dotnet/blob/main/docs/architecture/hybrid-codegen.md)
- [Known Limitations](https://github.com/thomaslaich/smithy-dotnet/blob/main/docs/known-limitations.md)
- [Roadmap](https://github.com/thomaslaich/smithy-dotnet/blob/main/docs/planning/roadmap.md)
