# Quick Start

This guide uses local packages from this repository. Published preview packages
are not assumed.

## Install The Smithy CLI

`SmithyNet.MSBuild` invokes the Smithy CLI during `dotnet build`. The CLI is a
JVM tool, so Java must also be available. Two recommended approaches are shown
below.

### Using pixi (conda-forge)

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
  <PackageReference Include="SmithyNet.Client" Version="0.1.0-preview.2" />
  <PackageReference Include="SmithyNet.Core" Version="0.1.0-preview.2" />
  <PackageReference Include="SmithyNet.Http" Version="0.1.0-preview.2" />
  <PackageReference Include="SmithyNet.Json" Version="0.1.0-preview.2" />
  <PackageReference Include="SmithyNet.MSBuild" Version="0.1.0-preview.2" PrivateAssets="all" />
</ItemGroup>
```

For generated ASP.NET Core `simpleRestJson` servers, also reference:

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
  <PackageReference Include="SmithyNet.Server" Version="0.1.0-preview.2" />
  <PackageReference Include="SmithyNet.Server.AspNetCore" Version="0.1.0-preview.2" />
</ItemGroup>
```

If the repo-level `Directory.Packages.props` applies to the example project and
you want explicit package versions in the project file, set:

```xml
<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
```

## Add A Model

For `restJson1`, the Smithy CLI needs the AWS protocol trait model. Add a
`smithy-build.json`:

```json
{
  "version": "1.0",
  "sources": ["model"],
  "maven": {
    "dependencies": ["software.amazon.smithy:smithy-aws-traits:1.68.0"]
  }
}
```

Example model:

```smithy
$version: "2"

namespace example.hello

use aws.protocols#restJson1

@restJson1
service HelloService {
    version: "2024-01-01"
    operations: [SayHello, Ping]
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

Limit generated C# to your service namespace when build dependencies contain
trait models that should not become C# types:

```xml
<PropertyGroup>
  <SmithyGeneratedNamespaces>example.hello</SmithyGeneratedNamespaces>
</PropertyGroup>
```

## Use The Generated Client

After `dotnet build`, generated files are under `obj/<configuration>/<tfm>/Smithy/`.

Generated service clients are named after the Smithy service:

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

The polyglot example at `examples/polyglot/dotnet` is the current end-to-end
consumer project. It generates .NET clients from the Java and Scala example
models and calls both APIs.

## Use The Generated Server

For `alloy#simpleRestJson`, generated services include operation-scoped handler
interfaces, an aggregate service handler interface, DI helpers, and an ASP.NET
Core endpoint mapper:

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

After generation, the compact path is one implementation of the aggregate
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
Smithy.NET ASP.NET Core server and a generated Smithy.NET client using the same
`alloy#simpleRestJson` model.

If you want to expose the same handler over both HTTP and gRPC, see
`docs/multi-protocol.md` and `examples/grpc/dotnet`.
