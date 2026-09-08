---
title: MSBuild reference
description: MSBuild properties and items for NSmithy.MSBuild.
---

`NSmithy.MSBuild` runs before C# compilation and drives the Smithy → C# code
generation pipeline. It invokes the `smithy` CLI, which runs the
`csharp-codegen` Java plugin to emit `.g.cs` files, then registers those files
with the .NET toolchain.

## Configuration sources

When a project supplies `smithy-build.json`, configure generator options in its
`csharp-codegen` plugin settings. Without one, NSmithy synthesizes the file from
MSBuild properties and model sources. See [Code generation](/smithy-dotnet/concepts/code-generation/).

## Properties

### Code generation

| Property | Default | Description |
| --- | --- | --- |
| `SmithyService` | — | Smithy shape ID of the service to generate (e.g. `example.hello#HelloService`). Required when synthesizing a `smithy-build.json` from a contracts reference. |
| `SmithyBaseNamespace` | — | Prefix prepended to generated C# namespaces. For example, `Acme` maps `example.library` to `Acme.Example.Library`. It does not filter model shapes. |
| `SmithyGenerateServer` | `true` | Emit server stub types. Set to `false` in client-only projects. |
| `SmithyGenerateClient` | `true` | Emit client types. Set to `false` in server-only projects. |
| `SmithyGenerateDependencyInjection` | `false` | Generate the `Add{Service}Client` IHttpClientFactory extension (flows into `smithy-build.json` as the `csharp-codegen` `generateDependencyInjection` setting). Requires referencing `Microsoft.Extensions.Http` (or the `Microsoft.AspNetCore.App` shared framework). See [Dependency Injection](/smithy-dotnet/guides/client-configuration/dependency-injection/). |
| `SmithyGenerateFakes` | `false` | Generate fake clients and handlers from modeled examples or deterministic placeholders. See [Fake handlers](/smithy-dotnet/servers/fake-handlers/) for response selection. |
| `SmithyBuildFile` | `$(MSBuildProjectDirectory)/smithy-build.json` | Path to the Smithy build configuration file. When absent, NSmithy can synthesize one under `obj/` from `SmithySource` and `SmithyContractsBuildFile` items. |
| `SmithyProjection` | `source` | Smithy build projection to use. |
| `SmithyPlugin` | `csharp-codegen` | Smithy build plugin name. |
| `SmithyBuildOutputPath` | `$(IntermediateOutputPath)Smithy/` | Root directory for all Smithy build output. |
| `SmithyStampFile` | `$(SmithyBuildOutputPath)NSmithy.Generated.stamp` | Incremental build stamp file. Smithy codegen is skipped when inputs have not changed since this file was last written. |
| `SmithyEmitGeneratedFiles` | `false` | Show generated `.g.cs` files in IDE project views when `true`. |
| `SmithyCliPath` | bundled CLI | Smithy CLI executable. Set this to override the bundled executable. |

### Documentation

| Property | Default | Description |
| --- | --- | --- |
| `SmithyGenerateDocs` | `false` | Generate Sphinx HTML documentation. Requires Python 3.11+. |
| `SmithyOpenApiProtocol` | — | Protocol shape ID for OpenAPI generation. |

See [Endpoint documentation](/smithy-dotnet/guides/endpoint-documentation/) for setup.

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
| `SmithyContractsBuildFile` | Contract build configuration used when synthesizing a consumer build file. Collected automatically from a contracts project reference; set explicitly for packaged models. |

## Contract inputs

A contracts project reference supplies model sources and its `smithy-build.json`
automatically. NuGet packages require explicit `SmithySource` and
`SmithyContractsBuildFile` items; a package reference alone is insufficient.
See [Distributing contracts](/smithy-dotnet/guides/distributing-contracts/)
for both configurations.

Declare Maven dependencies in `smithy-build.json` under `maven.dependencies`.
There is no `SmithyMavenDependency` MSBuild item. Build-file synthesis reads one
contract build configuration; use an explicit build file when combining models
with separate configurations.

## Smithy CLI

NSmithy bundles the Smithy CLI inside `NSmithy.MSBuild` and
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

```xml
<PropertyGroup>
  <SmithyCliPath>/path/to/smithy</SmithyCliPath>
</PropertyGroup>
```
