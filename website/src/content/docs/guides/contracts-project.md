---
title: Contracts Project
description: Separate your Smithy model into a dedicated contracts project shared by server and client.
---

A contracts project is a class library that owns the Smithy model files and
exposes them to any project that references it — server, client, or both. The
consuming projects do not need a `smithy-build.json`; NSmithy synthesizes one
from the model sources and Maven dependencies collected from the contracts
reference.

## Why a Contracts Project

In the Quick Start, the model lives in the same project as the server or client.
That works for small cases, but becomes awkward when:

- a server project and a client project need to share the same model
- you want to version and publish the contract independently
- you want to distribute the model to non-.NET consumers (Maven/JAR)

A contracts project gives each concern its own csproj and lets the model be the
single source of truth.

## Create the Contracts Project

```shell
dotnet new classlib -n MyService.Contracts
```

Replace the generated `.csproj` contents with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="NSmithy.Contracts" Version="0.1.0-preview.8" />
  </ItemGroup>

  <!-- Maven dependencies needed by the Smithy codegen plugin -->
  <ItemGroup>
    <SmithyMavenDependency Include="com.disneystreaming.alloy:alloy-core:0.3.38" />
    <SmithyMavenDependency Include="io.github.thomaslaich.nsmithy:smithy-csharp-codegen:0.1.0-preview.8" />
  </ItemGroup>
</Project>
```

`NSmithy.Contracts` adds MSBuild targets that:

- expose `GetSmithyContractItems` — returns the `.smithy` files to codegen consumers
- expose `GetSmithyMavenDependencies` — forwards Maven dependency declarations
- embed model files into the NuGet package when `dotnet pack` is run

The contracts project itself does **not** run codegen; it only holds the model.
Delete any generated `Class1.cs` file.

## Add the Model

By default, NSmithy looks for `.smithy` files under `model/` (relative to the
`.csproj`). Create `model/hello.smithy`:

```smithy
$version: "2"

namespace example.hello

use alloy#simpleRestJson

@simpleRestJson
service HelloService {
    version: "2026-01-01"
    operations: [SayHello]
}

@http(method: "GET", uri: "/hello/{name}")
@readonly
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

Override the model root with `<SmithySources>` if your layout differs:

```xml
<PropertyGroup>
  <SmithySources>$(MSBuildProjectDirectory)/api</SmithySources>
</PropertyGroup>
```

## Reference from a Consuming Project

Add a `ProjectReference` to the contracts project and set `SmithyService` to
tell NSmithy which service to generate:

```xml
<PropertyGroup>
  <SmithyService>example.hello#HelloService</SmithyService>
</PropertyGroup>

<ItemGroup>
  <!-- server (ASP.NET Core) -->
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
  <PackageReference Include="NSmithy.Server.AspNetCore" Version="0.1.0-preview.8" />

  <!-- or client -->
  <PackageReference Include="NSmithy.Client" Version="0.1.0-preview.8" />

  <ProjectReference Include="../MyService.Contracts/MyService.Contracts.csproj" />
</ItemGroup>
```

No `smithy-build.json` is needed. On `dotnet build`, NSmithy:

1. Calls `GetSmithyContractItems` on the contracts project to collect `.smithy` files
2. Calls `GetSmithyMavenDependencies` to collect Maven dependencies
3. Synthesizes a `smithy-build.json` under `obj/` from those inputs
4. Invokes `smithy build` and adds the generated `.g.cs` files to compilation

Multiple projects can reference the same contracts project; each gets its own
synthesized build file and generated output under `obj/`.

## Restrict Generated Output

When a contracts project references trait libraries (such as `alloy-core`), those
namespaces are part of the assembled model but usually should not be emitted as
C# types. Use `SmithyBaseNamespace` to emit only shapes under your own namespace:

```xml
<PropertyGroup>
  <SmithyService>example.hello#HelloService</SmithyService>
  <SmithyBaseNamespace>example.hello</SmithyBaseNamespace>
</PropertyGroup>
```

See the [MSBuild Reference](/smithy-dotnet/reference/msbuild/) for the full
property list.

## Related

- [Distributing Contracts](/smithy-dotnet/guides/distributing-contracts/) — publish the contracts package to NuGet and Maven
- [MSBuild Reference](/smithy-dotnet/reference/msbuild/) — all MSBuild properties and items
