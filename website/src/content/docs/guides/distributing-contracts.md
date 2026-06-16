---
title: Distributing Contracts
description: Share your Smithy model across projects and ecosystems via a NuGet contracts package or a Maven-compatible JAR.
---

Distributing your Smithy model lets other projects consume it without copying
files. There are two distribution paths:

- **Maven JAR** — any Smithy-based toolchain (Java, TypeScript, Python, and .NET)
  can consume it from a Maven registry, making it the more universal option.
- **NuGet package** — .NET consumers reference it like any other package; NSmithy
  picks up the model files and synthesizes a `smithy-build.json` automatically.

## Maven JAR Distribution

:::tip[Recommended]
Maven JAR distribution is generally preferred — it works with any Smithy-based
toolchain, not just .NET, so your model remains accessible to Java, TypeScript,
Python, and other consumers without any extra steps.
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

:::note
Maven JAR distribution does not require a dedicated contracts project. You can
add `SmithyMavenGroupId`, `SmithyMavenArtifactId`, and `SmithyPublish=true`
directly to a server project that already owns its model files and `dotnet pack`
will produce the JAR from there. That said, we still recommend splitting the
model out into a separate contracts project — it keeps the model decoupled from
any one implementation and makes it easier to share across server and client
projects.
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
dependencies automatically — no `ProjectReference` needed:

```xml
<PropertyGroup>
  <SmithyService>example.hello#HelloService</SmithyService>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="NSmithy.Server.AspNetCore" Version="0.2.0" />
  <PackageReference Include="MyService.Contracts" Version="1.0.0" />
</ItemGroup>
```

NSmithy synthesizes a `smithy-build.json` under `obj/`, invokes `smithy build`,
and adds the generated `.g.cs` files to compilation — identical behaviour to a
`ProjectReference`.
