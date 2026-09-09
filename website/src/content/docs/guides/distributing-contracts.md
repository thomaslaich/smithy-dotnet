---
title: Distributing contracts
description: Share your Smithy model across projects and ecosystems via a NuGet contracts package or a Maven-compatible JAR.
---

Distributing your Smithy model lets other projects consume the contract without
copying model files. NSmithy supports two packaging paths:

- **Maven JAR** — any Smithy-based toolchain (Java, TypeScript, Python, .NET,
  and others) can consume it from a Maven registry. This is the more portable
  option.
- **NuGet package** — contains model files and their Smithy build configuration.
  Consumers currently need explicit MSBuild items to import them.

## Within a solution

Reference a contracts project with `SmithyPublish=true`:

```xml
<ItemGroup>
  <ProjectReference Include="../MyService.Contracts/MyService.Contracts.csproj" />
</ItemGroup>
```

Set `SmithyService` in the consumer to select its service. Without an explicit
`smithy-build.json`, NSmithy assembles the referenced sources automatically.

## Maven JAR distribution

:::tip[Recommended]
Maven JAR distribution is generally preferred. It works with Smithy tooling
outside .NET, so Java, TypeScript, Python, and other consumers can use the same
model package.
:::

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

The output directory contains the NuGet package and the Maven JAR, POM, and
checksum files. The JAR contains the model in Smithy's discovery layout.

### Install locally

To make the JAR available to local Smithy CLI builds during development:

```shell
mvn install:install-file \
  -Dfile=MyService.Contracts/bin/Release/my-service-contracts-1.0.0.jar \
  -DpomFile=MyService.Contracts/bin/Release/my-service-contracts-1.0.0.pom \
  -Dpackaging=jar
```

### Publish to a registry

Use the `dotnet-nsmithy push` tool to upload to a Maven registry that accepts
HTTP PUT, such as GitHub Packages, Artifactory, or Nexus:

```shell
dotnet tool install -g dotnet-nsmithy

dotnet nsmithy push MyService.Contracts/bin/Release \
  --project MyService.Contracts/MyService.Contracts.csproj \
  --registry https://maven.pkg.github.com/ORG/REPO \
  --username $GITHUB_ACTOR \
  --token    $GITHUB_TOKEN
```

`push` reads the Maven coordinates and version from the `.csproj` automatically.
Credentials can also be supplied via `MAVEN_USERNAME` / `MAVEN_TOKEN` environment
variables.

:::note
Maven JAR distribution does not require a dedicated contracts project. You can
add `SmithyMavenGroupId`, `SmithyMavenArtifactId`, and `SmithyPublish=true`
directly to a server project that owns model files, and `dotnet pack` will
produce the JAR. A separate contracts project is still recommended because it
keeps the model decoupled from any one implementation and easier to share across
server and client projects.
:::

### Consume

The client template defaults to Maven distribution:

```shell
dotnet new nsmithy-client -n MyService.Client
```

Replace the placeholder contracts coordinate in its `smithy-build.json` with
`io.github.acme:my-service-contracts:1.0.0`. Configure `maven.repositories` in
that file for a private registry. Other Smithy toolchains consume the same
coordinate through their Maven dependency configuration.

## NuGet distribution

### Pack

```shell
dotnet pack MyService.Contracts --configuration Release
```

With `SmithyPublish=true`, the package contains `build/smithy/` model sources
and `build/smithy-build.json`. Publish the `.nupkg` to your NuGet feed.

### Consume

:::note[Current limitation]
NuGet contract packages currently require explicit model imports. Automatic
imports through a normal `PackageReference` are an intended improvement.
The configuration below is the current workaround.
:::

Request the package's resolved path and supply both the model sources and
packaged build configuration:

```xml
<PropertyGroup>
  <SmithyService>example.hello#HelloService</SmithyService>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="NSmithy.MSBuild" Version="NSMITHY_VERSION" PrivateAssets="all" />
  <PackageReference Include="MyService.Contracts" Version="1.0.0" GeneratePathProperty="true" />
  <SmithySource Include="$(PkgMyService_Contracts)/build/smithy/**/*.smithy" />
  <SmithyContractsBuildFile Include="$(PkgMyService_Contracts)/build/smithy-build.json" />
</ItemGroup>
```

NuGet replaces dots with underscores in the generated package-path property.
Adjust `PkgMyService_Contracts` when using a different package ID.

Keep the runtime packages required by the generated client or server. When no
explicit build file exists, NSmithy synthesizes `smithy-build.json` under `obj/`,
invokes `smithy build`,
and adds the generated `.g.cs` files to compilation.
