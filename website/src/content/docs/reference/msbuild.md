---
title: MSBuild Reference
description: MSBuild properties and items for NSmithy.MSBuild.
---

`NSmithy.MSBuild` runs before C# compilation and drives the Smithy → C# code
generation pipeline. It invokes the `smithy` CLI, which runs the
`csharp-codegen` Java plugin to emit `.g.cs` files, then registers those files
with the .NET toolchain.

## Properties

### Code generation

| Property | Default | Description |
| --- | --- | --- |
| `SmithyService` | — | Smithy shape ID of the service to generate (e.g. `example.hello#HelloService`). Required when synthesizing a `smithy-build.json` from a contracts reference. |
| `SmithyBaseNamespace` | — | Restricts generated C# types to shapes whose Smithy namespace starts with this value. Leave empty to emit all shapes. |
| `SmithyGenerateServer` | `true` | Emit server stub types. Set to `false` in client-only projects. |
| `SmithyGenerateClient` | `true` | Emit client types. Set to `false` in server-only projects. |
| `SmithyGenerateDependencyInjection` | `false` | Generate the `Add{Service}Client` IHttpClientFactory extension (flows into `smithy-build.json` as the `csharp-codegen` `generateDependencyInjection` setting). Requires referencing `Microsoft.Extensions.Http` (or the `Microsoft.AspNetCore.App` shared framework). See [Dependency Injection](/smithy-dotnet/guides/client-configuration/dependency-injection/). |
| `SmithyBuildFile` | `$(MSBuildProjectDirectory)/smithy-build.json` | Path to the Smithy build configuration file. When absent and `SmithySource` items are present, NSmithy synthesizes one under `obj/`. |
| `SmithyProjection` | `source` | Smithy build projection to use. |
| `SmithyPlugin` | `csharp-codegen` | Smithy build plugin name. |
| `SmithyBuildOutputPath` | `$(IntermediateOutputPath)Smithy/` | Root directory for all Smithy build output. |
| `SmithyStampFile` | `$(SmithyBuildOutputPath)NSmithy.Generated.stamp` | Incremental build stamp file. Smithy codegen is skipped when inputs have not changed since this file was last written. |
| `SmithyEmitGeneratedFiles` | `false` | Show generated `.g.cs` files in IDE project views when `true`. |
| `SmithyCliPath` | bundled CLI | Smithy CLI executable. Set this to override the bundled executable. |

### gRPC / Protobuf

| Property | Default | Description |
| --- | --- | --- |
| `SmithyGrpcServices` | `Both` | Passed as `GrpcServices` to Grpc.Tools when `.proto` files are generated. Valid values: `Both`, `Client`, `Server`, `None`. |

### Publishing (SmithyPublish=true)

| Property | Default | Description |
| --- | --- | --- |
| `SmithyPublish` | `false` | Pack `.smithy` model files into the NuGet package and (when `SmithyMavenGroupId` is set) produce a Maven JAR on `dotnet pack`. |
| `SmithySources` | `$(MSBuildProjectDirectory)/model` | Directory containing the `.smithy` source files to publish. |
| `SmithyMavenGroupId` | — | Maven `groupId` for the emitted JAR (e.g. `io.github.acme`). When set, `dotnet pack` produces a JAR alongside the `.nupkg`. |
| `SmithyMavenArtifactId` | — | Maven `artifactId` for the emitted JAR (e.g. `my-service-contracts`). Required when `SmithyMavenGroupId` is set. |

## Items

| Item | Description |
| --- | --- |
| `SmithySource` | `.smithy` files to include in the synthesized `smithy-build.json`. Populated automatically from a `ProjectReference` to a project with `SmithyPublish=true`, or added manually for advanced cases. |
| `SmithyMavenDependency` | Maven coordinates of Smithy model JARs needed by the Smithy CLI (e.g. `com.disneystreaming.alloy:alloy-core:0.3.38`). These are written into the synthesized `smithy-build.json` and, in publishing projects, into the NuGet package for downstream consumers. |

## Mixing model sources

A single project can consume Smithy models from multiple sources simultaneously.
`SmithySource` items and `SmithyMavenDependency` items are additive — NSmithy
collects them all and writes a single synthesized `smithy-build.json`:

```xml
<!-- Model files from a contracts project in the same repo -->
<ProjectReference Include="../ServiceA.Contracts/ServiceA.Contracts.csproj" />

<!-- Model JAR from a Maven registry -->
<SmithyMavenDependency Include="io.github.acme:service-b-contracts:1.0.0" />
```

`ProjectReference` items are the recommended way to consume contracts within
a solution. `SmithyMavenDependency` covers external dependencies — whether
published by a team using NSmithy or any other Smithy toolchain.

:::note
Consuming a contracts model from a **published NuGet package** (rather than a
`ProjectReference`) is not yet supported. Use `SmithyMavenDependency` for
cross-team model sharing outside the solution.
:::

## Smithy CLI

NSmithy bundles the Smithy CLI (version 1.73.0) inside `NSmithy.MSBuild` and
selects the correct platform binary automatically. No separate installation is
required. The bundle is self-contained and includes a JRE, so Java does not
need to be installed either.

NSmithy.MSBuild also bundles the NSmithy Smithy codegen plugins plus the common
Smithy and alloy trait/doc/openapi dependencies used by the templates and
examples. Additional Maven dependencies declared in `smithy-build.json` are not
mirrored into the package; they remain the consuming project's responsibility
and may require access to the configured Maven repositories.

Set `SmithyCliPath` to override the bundled binary with a specific executable,
for example when testing against a different CLI version:
