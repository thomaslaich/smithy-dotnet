---
title: Quick Start
description: Get up and running with NSmithy.
---

> **Note on the Java dependency:** `NSmithy.MSBuild` invokes the Smithy CLI
> during `dotnet build`. The CLI is a JVM tool, so Java must be available on
> the machine. This is the most common adoption blocker for .NET teams — if
> your team has no existing Java toolchain, setting it up just to run a code
> generator can feel heavyweight. The fastest way around this is the
> [smithy-dotnet-minimal-pixi](https://github.com/thomaslaich/smithy-dotnet-minimal-pixi)
> example, which uses [pixi](https://pixi.sh) to manage the Smithy CLI, Java,
> and .NET in a self-contained project-local environment — no system-wide Java
> install required.

This guide uses local packages from this repository. Published preview packages
are not assumed.

## Install The Smithy CLI

Two recommended approaches are shown below.

### Using pixi (conda-forge, recommended)

[pixi](https://pixi.sh) manages both the Smithy CLI and the JDK in a
reproducible conda-forge environment.

**1. Initialise the environment and add dependencies:**

```bash
pixi init
pixi add smithy openjdk dotnet
```

**2. Wire up `JAVA_HOME`.**

The `smithy` CLI needs `JAVA_HOME` to point at the JDK bundled inside the
pixi environment. Add the following to `pixi.toml`:

```toml
[activation.env]
JAVA_HOME = "$CONDA_PREFIX/lib/jvm"

[activation]
scripts = ["scripts/activate-java.sh"]
```

Create `scripts/activate-java.sh`:

```bash
#!/usr/bin/env bash
export PATH="$JAVA_HOME/bin:$PATH"
```

**3. Enter the environment and build:**

```bash
pixi shell
dotnet build
```

When the environment is active, `smithy` is resolved from `PATH`. You only need
to set `SmithyCliPath` when the build does not inherit the intended `PATH` or
when you want to force a specific executable:

```xml
<PropertyGroup>
  <SmithyCliPath>.pixi/envs/default/bin/smithy</SmithyCliPath>
</PropertyGroup>
```

### Using devenv

[devenv](https://devenv.sh) is a Nix-based alternative. This repository itself
uses devenv — see `devenv.nix` and `devenv.yaml` at the repo root for a working
reference. The key pieces are enabling the `languages.java` and `languages.dotnet`
options and adding a custom Nix derivation for the Smithy CLI; devenv then sets
`JAVA_HOME` and `PATH` automatically when you enter the shell with
`devenv shell` (or via direnv).

## Configure A Consumer Project

Reference the packages needed by generated `restJson1` clients:

```xml
<ItemGroup>
  <PackageReference Include="NSmithy.Client" Version="0.1.0-preview.5" />
  <PackageReference Include="NSmithy.Core" Version="0.1.0-preview.5" />
  <PackageReference Include="NSmithy.Http" Version="0.1.0-preview.5" />
  <PackageReference Include="NSmithy.Codecs.Json" Version="0.1.0-preview.5" />
  <PackageReference Include="NSmithy.MSBuild" Version="0.1.0-preview.5" PrivateAssets="all" />
</ItemGroup>
```

For generated ASP.NET Core `simpleRestJson` servers, also reference:

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
  <PackageReference Include="NSmithy.Server" Version="0.1.0-preview.5" />
  <PackageReference Include="NSmithy.Server.AspNetCore" Version="0.1.0-preview.5" />
</ItemGroup>
```

If the repo-level `Directory.Packages.props` applies to the example project and
you want explicit package versions in the project file, set:

```xml
<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
```

## Add A Model

For NSmithy code generation, add a `smithy-build.json` like this:

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

Example model:

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

@http(method: "POST", uri: "/ping")
operation Ping {
    input := {
        @required
        name: String
    }

    output := {
        @required
        message: String
    }
}
```

## Use The Generated Client

After `dotnet build`, generated files are under `obj/<configuration>/<tfm>/Smithy/`.

Generated service clients are named after the Smithy service:

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

The polyglot example at `examples/polyglot/dotnet` is the current end-to-end
consumer project. It generates .NET clients from the Java and Scala example
models and calls both APIs.

## Use The Generated Server

For `alloy#simpleRestJson`, generated services include operation-scoped handler
interfaces, an aggregate service handler interface, DI helpers, and an ASP.NET
Core endpoint mapper. After generation, the compact path is one implementation of the aggregate
service handler interface:

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
    {
        return Task.FromResult(new SayHelloOutput($"hello, {input.Name}"));
    }

    public Task<PingOutput> PingAsync(
        PingInput input,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PingOutput($"pong, {input.Name}"));
    }
}
```

For larger services, register operation handlers separately:

```csharp
builder.Services.AddSingleton<ISayHelloHandler, SayHelloHandler>();
builder.Services.AddSingleton<IPingHandler, PingHandler>();

internal sealed class SayHelloHandler : ISayHelloHandler
{
    public Task<SayHelloOutput> SayHelloAsync(
        SayHelloInput input,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new SayHelloOutput($"hello, {input.Name}"));
    }
}

internal sealed class PingHandler : IPingHandler
{
    public Task<PingOutput> PingAsync(
        PingInput input,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PingOutput($"pong, {input.Name}"));
    }
}
```

The example at `examples/simple-rest-json/dotnet` shows a generated
NSmithy ASP.NET Core server and a generated NSmithy client using the same
`alloy#simpleRestJson` model.

For the new binary codec path, `examples/rpcv2cbor/dotnet` shows a generated
`smithy.protocols#rpcv2Cbor` client talking to an in-process mock transport.

If you want to expose the same handler over both HTTP and gRPC, see
[Multi-Protocol](./multi-protocol/) and `examples/grpc/dotnet`.
