---
title: Distributing Contracts
description: Share your Smithy model across projects and ecosystems via a NuGet contracts package or a Maven-compatible JAR.
---

Distributing your Smithy model lets other projects consume the contract without
copying model files. NSmithy supports two packaging paths:

- **Maven JAR** — any Smithy-based toolchain (Java, TypeScript, Python, .NET,
  and others) can consume it from a Maven registry. This is the more portable
  option.
- **NuGet package** — .NET consumers reference it like any other package; NSmithy
  picks up the model files and synthesizes a `smithy-build.json` automatically.

## Maven JAR Distribution

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

When `SmithyMavenGroupId` is set, the `_CreateSmithyJar` MSBuild target runs
after `Pack` and writes Maven artifacts alongside the `.nupkg`:

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

To make the JAR available to local Smithy CLI builds during development:

```shell
mvn install:install-file \
  -Dfile=bin/Release/my-service-contracts-1.0.0.jar \
  -DpomFile=bin/Release/my-service-contracts-1.0.0.pom \
  -Dpackaging=jar
```

### Publish to a Remote Registry

Use the `dotnet-nsmithy push` tool to upload to a Maven registry that accepts
HTTP PUT, such as GitHub Packages, Artifactory, or Nexus:

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

:::note
Maven JAR distribution does not require a dedicated contracts project. You can
add `SmithyMavenGroupId`, `SmithyMavenArtifactId`, and `SmithyPublish=true`
directly to a server project that owns model files, and `dotnet pack` will
produce the JAR. A separate contracts project is still recommended because it
keeps the model decoupled from any one implementation and easier to share across
server and client projects.
:::

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
dependencies automatically:

```xml
<PropertyGroup>
  <SmithyService>example.hello#HelloService</SmithyService>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="NSmithy.Server.AspNetCore" Version="0.5.0" />
  <PackageReference Include="MyService.Contracts" Version="1.0.0" />
</ItemGroup>
```

NSmithy synthesizes a `smithy-build.json` under `obj/`, invokes `smithy build`,
and adds the generated `.g.cs` files to compilation — identical behavior to a
`ProjectReference`.
