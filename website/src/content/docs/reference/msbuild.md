---
title: MSBuild reference
description: MSBuild properties and items for NSmithy.MSBuild.
---

`NSmithy.MSBuild` runs before C# compilation and drives the Smithy → C# code
generation pipeline. It invokes the `smithy` CLI, which runs the
`csharp-codegen` Java plugin to emit `.g.cs` files, then registers those files
with the .NET toolchain.

## Configuration sources

Without a `smithy-build.json` at the project root, NSmithy synthesizes one under
`obj/` from MSBuild properties and the contracts reference. With one, NSmithy
runs it as is. See [Code generation](/smithy-dotnet/concepts/code-generation/).

Properties marked **Synthesized only** feed the synthesized file and have no
effect next to your own `smithy-build.json`; set the named `csharp-codegen`
plugin setting there instead. NSmithy warns with `NSMITHYIGNORED` when one of
them would change the generated code. All other properties apply in both cases.

## Properties

### Code generation

| Property | Description |
| --- | --- |
| `SmithyService` | Smithy shape ID of the service to generate (e.g. `example.hello#HelloService`). Required when synthesizing a `smithy-build.json` from a contracts reference. **Synthesized only:** `service`. |
| `SmithyBaseNamespace` | Prefix prepended to generated C# namespaces. For example, `Acme` maps `example.library` to `Acme.Example.Library`. It does not filter model shapes. **Synthesized only:** `baseNamespace`. |
| `SmithyGenerateServer` | Emit server stub types. Set to `false` in client-only projects. With your own `smithy-build.json`, it selects whether the generated server files are compiled. Default: `true`. |
| `SmithyGenerateClient` | Emit client types. Set to `false` in server-only projects. With your own `smithy-build.json`, it selects whether the generated client files are compiled. Default: `true`. |
| `SmithyGenerateDependencyInjection` | Generate the `Add{Service}Client` IHttpClientFactory extension. Requires referencing `Microsoft.Extensions.Http` (or the `Microsoft.AspNetCore.App` shared framework). See [Dependency Injection](/smithy-dotnet/guides/client-configuration/dependency-injection/). Default: `false`. **Synthesized only:** `generateDependencyInjection`. |
| `SmithyGenerateFakes` | Generate fake clients and handlers from modeled examples or deterministic placeholders. See [Fake handlers](/smithy-dotnet/servers/fake-handlers/) for response selection. Default: `false`. **Synthesized only:** `generateFakes`. |
| `SmithyBuildFile` | Path to the Smithy build configuration file. When absent, NSmithy can synthesize one under `obj/` from `SmithySource` and `SmithyContractsBuildFile` items. Default: `$(MSBuildProjectDirectory)/smithy-build.json`. |
| `SmithyProjection` | Smithy build projection to use. Default: `source`. |
| `SmithyPlugin` | Smithy build plugin name. Default: `csharp-codegen`. |
| `SmithyBuildOutputPath` | Root directory for all Smithy build output. Default: `$(IntermediateOutputPath)Smithy/`. |
| `SmithyStampFile` | Incremental build stamp file. Smithy codegen is skipped when inputs have not changed since this file was last written. Default: `$(SmithyBuildOutputPath)NSmithy.Generated.stamp`. |
| `SmithyEmitGeneratedFiles` | Show generated `.g.cs` files in IDE project views when `true`. Default: `false`. |

### Smithy CLI

See [Smithy CLI](/smithy-dotnet/reference/smithy-cli/).

| Property | Description |
| --- | --- |
| `SmithyCliPath` | Smithy CLI executable. Takes precedence over the `NSmithy.SmithyCli` package and `smithy` on `PATH`. |
| `SmithyVersion` | Smithy version NSmithy is built against. The Smithy CLI must have this version, and `software.amazon.smithy` Maven dependencies should use it. Default: `1.73.0`. |
| `SmithyCliVersionCheck` | Fail the build when the Smithy CLI's version differs from `SmithyVersion`. Default: `true`. |

### Maven dependencies

See [Maven dependencies](/smithy-dotnet/reference/maven-dependencies/).

| Property | Description |
| --- | --- |
| `SmithyUseBundledMavenRepo` | Resolve NSmithy's plugins and the bundled trait packages from the Maven repository inside `NSmithy.MSBuild`. Set to `false` to resolve everything from Maven repositories, as the plain Smithy CLI does. Default: `true`. |

### Documentation

| Property | Description |
| --- | --- |
| `SmithyGenerateDocs` | Generate Sphinx HTML documentation and copy it to `wwwroot/docs/`. Requires Python 3.11+. With your own `smithy-build.json`, add the `docgen` plugin there; the property still copies the output. Default: `false`. |
| `SmithyOpenApiProtocol` | Protocol shape ID for OpenAPI generation; the output is copied to `wwwroot/openapi.json`. With your own `smithy-build.json`, add the `openapi` plugin there; the property still copies the output. |

See [Endpoint documentation](/smithy-dotnet/guides/endpoint-documentation/) for setup.

### gRPC / Protobuf

| Property | Description |
| --- | --- |
| `SmithyGrpcServices` | Passed as `GrpcServices` to Grpc.Tools when `.proto` files are generated. Valid values: `Both`, `Client`, `Server`, `None`. Default: `Both`. |

### Publishing (SmithyPublish=true)

| Property | Description |
| --- | --- |
| `SmithyPublish` | Pack `.smithy` model files into the NuGet package and (when `SmithyMavenGroupId` is set) produce a Maven JAR on `dotnet pack`. Default: `false`. |
| `SmithySources` | Directory containing the `.smithy` source files to publish. Default: `$(MSBuildProjectDirectory)/model`. |
| `SmithyMavenGroupId` | Maven `groupId` for the emitted JAR (e.g. `io.github.acme`). When set, `dotnet pack` produces a JAR alongside the `.nupkg`. |
| `SmithyMavenArtifactId` | Maven `artifactId` for the emitted JAR (e.g. `my-service-contracts`). Required when `SmithyMavenGroupId` is set. |

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

Declare Maven dependencies in `smithy-build.json` under `maven.dependencies`;
see [Maven dependencies](/smithy-dotnet/reference/maven-dependencies/) for
where they resolve from.
There is no `SmithyMavenDependency` MSBuild item. Build-file synthesis reads one
contract build configuration; use an explicit build file when combining models
with separate configurations.
