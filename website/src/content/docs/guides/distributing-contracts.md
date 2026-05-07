---
title: Distributing Contracts
description: Publish a contracts package to NuGet and produce a Maven-compatible JAR for cross-ecosystem consumers.
---

A [contracts project](/smithy-dotnet/guides/contracts-project/) can be packed
and published so teams outside the solution can consume the model — .NET
consumers via NuGet, and Java/Smithy consumers via a Maven-compatible JAR. Both
artifacts are produced by a single `dotnet pack` invocation.

## NuGet Distribution

### Packing

```shell
dotnet pack MyService.Contracts --configuration Release
```

`NSmithy.Contracts` embeds the `.smithy` model files and the Maven dependency
list into the package at pack time:

| Package path | Contents |
| --- | --- |
| `build/smithy/**/*.smithy` | model files |
| `build/smithy-maven-deps.txt` | one Maven coordinate per line |
| `build/NSmithy.Contracts.props` | sets `SmithySources` default |
| `build/NSmithy.Contracts.targets` | MSBuild targets for the contracts project itself |
| `buildTransitive/NSmithy.Contracts.targets` | MSBuild targets imported by downstream NuGet consumers |

### Consuming via NuGet

A project that references the published package picks up the model
and Maven dependencies automatically through the `buildTransitive` targets. No
`ProjectReference` is needed:

```xml
<PropertyGroup>
  <SmithyService>example.hello#HelloService</SmithyService>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="NSmithy.Server.AspNetCore" Version="0.1.0-preview.8" />
  <PackageReference Include="MyService.Contracts" Version="1.0.0" />
</ItemGroup>
```

The behaviour is identical to a `ProjectReference`: NSmithy synthesizes a
`smithy-build.json` under `obj/`, calls `smithy build`, and adds the generated
`.g.cs` files to compilation.

## Maven JAR Distribution

Smithy's tooling — including the NSmithy codegen plugin — discovers model
dependencies by scanning JARs on the Maven classpath. To make the contracts
model consumable as a Smithy dependency (whether from Maven Central, a private
Artifactory, or a local `~/.m2` repository), `dotnet pack` can emit a
Maven-compatible JAR alongside the NuGet package.

### Configure the Project

Add `SmithyMavenGroupId` and `SmithyMavenArtifactId` to the contracts project:

```xml
<PropertyGroup>
  <PackageId>MyService.Contracts</PackageId>
  <Version>1.0.0</Version>

  <!-- Maven coordinates for the emitted JAR -->
  <SmithyMavenGroupId>io.github.acme</SmithyMavenGroupId>
  <SmithyMavenArtifactId>my-service-contracts</SmithyMavenArtifactId>
</PropertyGroup>
```

The `Version` property is reused for the Maven version — no separate setting is
needed.

### Pack

```shell
dotnet pack MyService.Contracts --configuration Release
```

When `SmithyMavenGroupId` is set, the `_CreateSmithyJar` target runs after
`Pack` and writes the following files next to the `.nupkg` in the output
directory:

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
  manifest          ← newline-delimited list of model file paths
  hello.smithy      ← model file(s) from model/
```

### Installing to the Local Maven Repository

To make the JAR available to a local Smithy CLI invocation during development:

```shell
mvn install:install-file \
  -Dfile=bin/Release/my-service-contracts-1.0.0.jar \
  -DpomFile=bin/Release/my-service-contracts-1.0.0.pom \
  -Dpackaging=jar
```

Once installed, the dependency can be added to any `smithy-build.json`:

```json
{
  "version": "1.0",
  "sources": ["model"],
  "maven": {
    "dependencies": [
      "io.github.acme:my-service-contracts:1.0.0"
    ]
  }
}
```

### Publishing to a Remote Registry

Upload the JAR, POM, and their `.md5`/`.sha1` checksums to your Maven registry
(Maven Central, GitHub Packages, Artifactory, etc.) following that registry's
deployment procedure. The checksum files satisfy Maven's artifact integrity
requirements out of the box.

## Related

- [Contracts Project](/smithy-dotnet/guides/contracts-project/) — set up and reference a contracts project
- [MSBuild Reference](/smithy-dotnet/reference/msbuild/) — all MSBuild properties and items
