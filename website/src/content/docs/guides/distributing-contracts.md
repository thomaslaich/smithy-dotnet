---
title: Distributing Contracts
description: Publish a contracts package to NuGet and produce a Maven-compatible JAR for cross-ecosystem consumers.
---

A [contracts project](/smithy-dotnet/guides/contracts-project/) can be packed
and published so teams outside the solution can consume the model — .NET
consumers via NuGet, and Java/Smithy consumers via a Maven-compatible JAR. Both
artifacts are produced by a single `dotnet pack` invocation.

**Maven is generally the more universal distribution path.** Any Smithy-based
toolchain — Java, TypeScript, Python, and .NET — can consume a JAR from a
Maven registry without any NSmithy-specific setup. NuGet distribution is a good
fit when your consumers are exclusively .NET and you would rather not maintain a
Maven registry at all; if you already publish to Maven (or use a public registry
like Maven Central), the JAR covers everyone and the NuGet package becomes
optional.

## NuGet Distribution

### Packing

```shell
dotnet pack MyService.Contracts --configuration Release
```

`NSmithy.MSBuild` embeds the `.smithy` model files and the Maven dependency
list into the package at pack time:

| Package path | Contents |
| --- | --- |
| `build/smithy/**/*.smithy` | model files |
| `build/smithy-maven-deps.txt` | one Maven coordinate per line |
| `build/NSmithy.MSBuild.props` | sets `SmithySources` default |
| `buildTransitive/NSmithy.MSBuild.targets` | MSBuild targets imported by all consumers |

### Consuming via NuGet

A project that references the published package picks up the model
and Maven dependencies automatically through the `buildTransitive` targets. No
`ProjectReference` is needed:

```xml
<PropertyGroup>
  <SmithyService>example.hello#HelloService</SmithyService>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="NSmithy.Server.AspNetCore" Version="0.1.0-preview.11" />
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
  <SmithyPublish>true</SmithyPublish>

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

Use the `dotnet-nsmithy push` tool to upload the JAR, POM, and checksums to any
Maven registry that accepts HTTP PUT (GitHub Packages, Artifactory, Nexus, etc.):

```shell
dotnet tool install -g dotnet-nsmithy

dotnet nsmithy push bin/Release \
  --registry https://maven.pkg.github.com/ORG/REPO \
  --username $GITHUB_ACTOR \
  --token    $GITHUB_TOKEN
```

`push` reads `SmithyMavenGroupId`, `SmithyMavenArtifactId`, and the version
from the `.csproj` automatically, so no extra flags are needed if you run it
from the project directory. Credentials can also be provided via the
`MAVEN_USERNAME` / `MAVEN_TOKEN` environment variables.

For Maven Central, follow Sonatype's deployment procedure — `push` targets
registries that accept direct HTTP PUT; the Central Portal's bundle-upload flow
requires a different approach.

## Related

- [Contracts Project](/smithy-dotnet/guides/contracts-project/) — set up and reference a contracts project
- [MSBuild Reference](/smithy-dotnet/reference/msbuild/) — all MSBuild properties and items
