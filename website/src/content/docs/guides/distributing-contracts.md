---
title: Distributing Contracts
description: Share your Smithy model across projects and ecosystems via a NuGet contracts package or a Maven-compatible JAR.
---

Distributing your Smithy model lets other projects consume it without copying
files. There are two distribution paths:

- **NuGet package** — .NET consumers reference it like any other package; NSmithy
  picks up the model files and synthesizes a `smithy-build.json` automatically.
- **Maven JAR** — any Smithy-based toolchain (Java, TypeScript, Python, and .NET)
  can consume it from a Maven registry, making it the more universal option.

## Create a Contracts Project

:::note[Optional]
A contracts project is not required for distribution. You only need one if you
want to distribute via NuGet, or if you simply prefer the separation of a
dedicated contracts project. For Maven JAR distribution you can add the relevant
properties directly to an existing server project that already owns its model files.
:::

```shell
dotnet new classlib -n MyService.Contracts
```

Replace the generated `.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <SmithyPublish>true</SmithyPublish>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="NSmithy.MSBuild" Version="0.1.0-preview.12" />
  </ItemGroup>

  <ItemGroup>
    <SmithyMavenDependency Include="com.disneystreaming.alloy:alloy-core:0.3.38" />
    <SmithyMavenDependency Include="io.github.thomaslaich.nsmithy:smithy-csharp-codegen:0.1.0-preview.12" />
  </ItemGroup>
</Project>
```

`SmithyPublish=true` activates the targets that expose model files to consumers
and embed them into the NuGet package at pack time. Delete any generated
`Class1.cs` — the contracts project holds only the model.

Place your model under `model/` (default) or set `<SmithySources>` to a
different path. Then add a `ProjectReference` from your server or client project:

```xml
<PropertyGroup>
  <SmithyService>example.hello#HelloService</SmithyService>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="NSmithy.Server.AspNetCore" Version="0.1.0-preview.12" />
  <ProjectReference Include="../MyService.Contracts/MyService.Contracts.csproj" />
</ItemGroup>
```

NSmithy synthesizes a `smithy-build.json` under `obj/` from the collected model
files and Maven dependencies and invokes `smithy build` automatically.

## NuGet Distribution

### Pack

```shell
dotnet pack MyService.Contracts --configuration Release
```

`NSmithy.MSBuild` embeds the model and dependency metadata into the package:

| Package path | Contents |
| --- | --- |
| `build/smithy/**/*.smithy` | model files |
| `build/smithy-maven-deps.txt` | one Maven coordinate per line |
| `build/NSmithy.MSBuild.props` | sets `SmithySources` for consumers |
| `buildTransitive/NSmithy.MSBuild.targets` | MSBuild targets imported transitively |

### Consume

A project that references the published package picks up the model and Maven
dependencies automatically — no `ProjectReference` needed:

```xml
<PropertyGroup>
  <SmithyService>example.hello#HelloService</SmithyService>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="NSmithy.Server.AspNetCore" Version="0.1.0-preview.12" />
  <PackageReference Include="MyService.Contracts" Version="1.0.0" />
</ItemGroup>
```

NSmithy synthesizes a `smithy-build.json` under `obj/`, invokes `smithy build`,
and adds the generated `.g.cs` files to compilation — identical behaviour to a
`ProjectReference`.

## Maven JAR Distribution

### Configure

Add `SmithyMavenGroupId` and `SmithyMavenArtifactId` to the contracts project:

```xml
<PropertyGroup>
  <SmithyPublish>true</SmithyPublish>
  <SmithyMavenGroupId>io.github.acme</SmithyMavenGroupId>
  <SmithyMavenArtifactId>my-service-contracts</SmithyMavenArtifactId>
</PropertyGroup>
```

`Version` (or `VersionPrefix`/`VersionSuffix`) is reused as the Maven version.

### Pack

```shell
dotnet pack MyService.Contracts --configuration Release
```

When `SmithyMavenGroupId` is set, the `_CreateSmithyJar` MSBuild target runs
after `Pack` and writes the JAR alongside the `.nupkg`:

```
bin/Release/
  MyService.Contracts.1.0.0.nupkg
  my-service-contracts-1.0.0.jar
  my-service-contracts-1.0.0.jar.md5
  my-service-contracts-1.0.0.jar.sha1
  my-service-contracts-1.0.0.pom
  my-service-contracts-1.0.0.pom.md5
  my-service-contracts-1.0.0.pom.sha1
```

The JAR follows Smithy's model-discovery layout:

```
META-INF/smithy/
  manifest        ← newline-delimited list of model file paths
  hello.smithy    ← model file(s) from model/
```

### Install Locally

To make the JAR available to a local Smithy CLI invocation during development:

```shell
mvn install:install-file \
  -Dfile=bin/Release/my-service-contracts-1.0.0.jar \
  -DpomFile=bin/Release/my-service-contracts-1.0.0.pom \
  -Dpackaging=jar
```

### Publish to a Remote Registry

Use the `dotnet-nsmithy push` tool to upload to any Maven registry that accepts
HTTP PUT (GitHub Packages, Artifactory, Nexus, etc.):

```shell
dotnet tool install -g dotnet-nsmithy

dotnet nsmithy push bin/Release \
  --registry https://maven.pkg.github.com/ORG/REPO \
  --username $GITHUB_ACTOR \
  --token    $GITHUB_TOKEN
```

`push` reads the Maven coordinates and version from the `.csproj` automatically.
Credentials can also be supplied via `MAVEN_USERNAME` / `MAVEN_TOKEN` environment
variables.
