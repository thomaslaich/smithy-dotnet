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
| `SmithyCliPath` | installed CLI cache | Use an existing executable; skips NSmithy installation and is never modified. |
| `SmithyCliCachePath` | user application-data directory + `NSmithy/smithy-cli` | Shared CLI cache. Also configurable with `NSMITHY_CLI_CACHE`. |
| `SmithyCliDownloadBaseUrl` | Smithy GitHub releases | Archive mirror base URL, also configurable with `NSMITHY_CLI_DOWNLOAD_BASE_URL`. |

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

Install the dotnet tool once, then restore and provision a solution before its
first build:

```shell
dotnet tool install --global dotnet-nsmithy
dotnet restore MySolution.slnx
dotnet nsmithy install --solution MySolution.slnx
dotnet build MySolution.slnx --no-restore
```

For a single project, use `dotnet nsmithy install --project MyService.csproj`
after `dotnet restore MyService.csproj`. If the tool is already installed,
`dotnet tool update --global dotnet-nsmithy` updates it to the latest release.

You can also install for one project without the dotnet tool:

```shell
dotnet restore MyService.csproj
dotnet msbuild MyService.csproj -t:InstallSmithyCli
```

The tool resolves C# project paths relative to the solution, including projects
in solution folders. It skips projects without the NSmithy install target and
runs each participating project's pinned installer. Projects requiring the same
CLI version reuse the cache; different versions are installed side by side.
An installation failure makes the command fail. Legacy `.sln` files are not parsed;
use `.slnx` or select individual projects.

With no argument, the tool selects the single `.slnx` in the current directory,
or the single `.csproj` if there are no XML solutions. Multiple candidates require
an explicit `--solution` or `--project`.

The project's restored `NSmithy.MSBuild` package pins the CLI version and SHA-256
checksums. The installer downloads only the host's official Smithy release archive,
including its Java runtime; a separate Java installation is unnecessary. Supported
hosts are macOS and Linux on x64/arm64, and Windows x64 (also used on Windows arm64).

Installations are shared between projects by CLI version and platform. Re-running
the command reuses a completed installation without network access. Run it again
after an NSmithy upgrade: it downloads only if the required CLI has changed.
Concurrent installers share a lock, and an interrupted or invalid download is not
published as a usable installation.

Neither NuGet restore nor normal builds download the CLI. A missing installation
reports the setup command. For CI or offline builds, run restore and installation
in the network-enabled stage and preserve the CLI cache for the build stage.
The default cache is under the user's local application-data directory
(`~/.local/share` on Linux). Set `SmithyCliCachePath` or `NSMITHY_CLI_CACHE` to use
another location, with the same setting during installation and builds.

For an internal mirror, set `SmithyCliDownloadBaseUrl` or
`NSMITHY_CLI_DOWNLOAD_BASE_URL`. The installer requests
`<base>/<version>/smithy-cli-<platform>.zip` and still verifies the pinned checksum.
Standard .NET HTTP proxy settings apply. NuGet feed configuration does not control
these archive downloads.

NSmithy.MSBuild also bundles the NSmithy Smithy codegen plugins plus the common
Smithy and alloy trait/doc/openapi dependencies used by the templates and
examples. Additional Maven dependencies declared in `smithy-build.json` are not
mirrored into the package; they remain the consuming project's responsibility
and may require access to the configured Maven repositories.

Set `SmithyCliPath` to use an existing executable instead. Both installation and
builds honor this override, including executables on read-only paths:

```xml title="MyService.csproj"
<PropertyGroup>
  <SmithyCliPath>/path/to/smithy</SmithyCliPath>
</PropertyGroup>
```
