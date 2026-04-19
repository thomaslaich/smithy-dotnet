# Quick Start

This guide uses local packages from this repository. Published preview packages
are not assumed.

## Install The Smithy CLI

`SmithyNet.MSBuild` invokes the Smithy CLI during `dotnet build`. The
recommended setup is to install `smithy-cli` in a project environment, such as
Pixi with the conda-forge package, and run builds through that environment:

```bash
pixi add smithy-cli
pixi run dotnet build
```

When the environment is active, `smithy` is resolved from `PATH`. You only need
to set `SmithyCliPath` when the build does not inherit the intended `PATH` or
when you want to force a specific executable:

```xml
<PropertyGroup>
  <SmithyCliPath>.pixi/envs/default/bin/smithy</SmithyCliPath>
</PropertyGroup>
```

## Create Local Packages

From the repository root:

```bash
dotnet pack SmithyNet.slnx --configuration Release --output artifacts/packages
```

The generated packages are written to `artifacts/packages`.

## Configure A Consumer Project

Add a `NuGet.config` next to the consumer project:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="smithy-net-local" value="../../artifacts/packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

Adjust the relative path for your project layout.

Reference the packages needed by generated `restJson1` clients:

```xml
<ItemGroup>
  <PackageReference Include="SmithyNet.Client" Version="1.0.0" />
  <PackageReference Include="SmithyNet.Core" Version="1.0.0" />
  <PackageReference Include="SmithyNet.Http" Version="1.0.0" />
  <PackageReference Include="SmithyNet.Json" Version="1.0.0" />
  <PackageReference Include="SmithyNet.MSBuild" Version="1.0.0" PrivateAssets="all" />
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

The polyglot example at `examples/polyglot/dotnet/client` is the current
end-to-end consumer project. It generates a .NET client from the Java example
model and calls the Java API.
