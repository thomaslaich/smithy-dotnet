---
title: MSBuild reference
description: MSBuild properties and items for NSmithy.MSBuild.
---

`NSmithy.MSBuild` runs before C# compilation and drives the Smithy → C# code
generation pipeline. It invokes the `smithy` CLI, which runs the
`csharp-codegen` Java plugin to emit `.g.cs` files, then registers those files
with the .NET toolchain.

## Toolchain

Two NuGet packages provide what generation needs:

- `NSmithy.MSBuild` contains the MSBuild integration, NSmithy's C# and Protobuf
  generators, the Smithy OpenAPI and documentation plugins, the trait libraries
  NSmithy uses, and their Java dependencies, as a local Maven repository inside
  the package.
- `NSmithy.SmithyCli` contains the Smithy CLI with its Java runtime. It is
  optional; see [Smithy CLI](/smithy-dotnet/reference/smithy-cli/).

MSBuild points the CLI at the bundled repository and an isolated Maven cache,
so NSmithy's plugins resolve from local files. Model dependencies a project
declares resolve separately; see
[Maven dependencies](/smithy-dotnet/reference/maven-dependencies/). Sphinx HTML
documentation additionally requires Python.

## Configuration sources

On every build, NSmithy writes an effective `smithy-build.json` to `obj/` and
runs the Smithy CLI on it. It combines:

- the project's own `smithy-build.json`, or an empty one when there is none;
- the model sources, Maven dependencies, repositories, and suppressions of
  referenced contracts;
- the plugin settings from the properties below.

The properties say what the project builds: the service, and whether it gets
the client, the server, docs, OpenAPI, or a `.proto` file. `smithy-build.json`
sets every other plugin setting, such as the
[`csharp-codegen` settings](#csharp-codegen-settings). A setting given both
ways fails the build. When no Maven repository is listed, Maven Central is.

## Properties

### Code generation

| Property | Description |
| --- | --- |
| `SmithyService` | Smithy shape ID of the service to generate (e.g. `example.hello#HelloService`). Turns on code generation from properties and sets `service` for each plugin NSmithy adds. Without it, configure NSmithy's plugins in `smithy-build.json`, for example one `csharp-codegen` per projection. |
| `SmithyGenerateServer` | Emit server stub types. Set to `false` in client-only projects; this sets `generateServer` to `false` and leaves generated server files out of compilation. Default: `true`. |
| `SmithyGenerateClient` | Emit client types. Set to `false` in server-only projects; this sets `generateClient` to `false` and leaves generated client files out of compilation. Default: `true`. |
| `SmithyBuildFile` | Path to the project's own Smithy build file, which NSmithy merges into the effective one. Optional. Default: `$(MSBuildProjectDirectory)/smithy-build.json`. |
| `SmithyProjection` | Projection that receives the plugin settings from properties, and whose docs and OpenAPI output is copied to `wwwroot/`. A projection other than `source` must exist in `smithy-build.json`. Default: `source`. |
| `SmithyPlugin` | Smithy build plugin name. Default: `csharp-codegen`. |
| `SmithyBuildOutputPath` | Root directory for all Smithy build output. Default: `$(IntermediateOutputPath)Smithy/`. |
| `SmithyStampFile` | Incremental build stamp file. Smithy codegen is skipped when inputs have not changed since this file was last written. Default: `$(SmithyBuildOutputPath)NSmithy.Generated.stamp`. |
| `SmithyEmitGeneratedFiles` | Show generated `.g.cs` files in IDE project views when `true`. Default: `false`. |

### csharp-codegen settings

Set these in `smithy-build.json`, under `plugins` or a projection's `plugins`:

| Setting | Description |
| --- | --- |
| `closure` | Id of a `shapeClosures` metadata entry to generate. Without `SmithyService`, only the closure's types are generated; with it, the closure must contain the service. See [Types without a service](/smithy-dotnet/reference/generated-code/#types-without-a-service). |
| `baseNamespace` | Prefix prepended to generated C# namespaces. For example, `Acme` maps `example.library` to `Acme.Example.Library`. It does not filter model shapes. |
| `defaultProtocol` | The protocol trait of the generated client's default protocol, such as `aws.protocols#restJson1`, for a service with several protocols. It must be one of the service's protocol traits. Default: the service's first protocol. |
| `generateDependencyInjection` | Generate the `Add{Service}Client` IHttpClientFactory extension. Requires referencing `Microsoft.Extensions.Http` (or the `Microsoft.AspNetCore.App` shared framework). See [Dependency Injection](/smithy-dotnet/guides/client-configuration/dependency-injection/). |
| `generateFakes` | Generate fake clients and handlers from modeled examples or deterministic placeholders. See [Fake handlers](/smithy-dotnet/servers/fake-handlers/). |

A project that generates from a contracts reference and has no
`smithy-build.json` adds one with only these settings:

```json title="smithy-build.json"
{
  "version": "1.0",
  "plugins": {
    "csharp-codegen": { "generateFakes": true, "baseNamespace": "Acme" }
  }
}
```

### Smithy CLI

See [Smithy CLI](/smithy-dotnet/reference/smithy-cli/).

| Property | Description |
| --- | --- |
| `SmithyCliPath` | Smithy CLI executable. Takes precedence over the `NSmithy.SmithyCli` package and `smithy` on `PATH`. |
| `SmithyVersion` | Smithy version NSmithy is built against. `SmithyCliVersionCheck` compares the Smithy CLI with it, and `software.amazon.smithy` Maven dependencies should use it. Default: `1.73.0`. |
| `SmithyCliVersionCheck` | How the Smithy CLI's version is checked against `SmithyVersion`: `exact`, `minimum` (that version or later), or `none`. Default: `exact` for `NSmithy.SmithyCli`, `minimum` for a CLI you provide. |

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
| `SmithySource` | `.smithy` files added to the sources of the effective `smithy-build.json`. Populated automatically from a `ProjectReference` to a project with `SmithyPublish=true`, or added manually for advanced cases. |
| `SmithyContractsBuildFile` | Contract build configuration whose Maven dependencies, repositories, and suppressions are merged into the effective `smithy-build.json`. Collected automatically from a contracts project reference; set explicitly for packaged models. |

## Contract inputs

A contracts project reference supplies model sources and its `smithy-build.json`
automatically. NuGet packages require explicit `SmithySource` and
`SmithyContractsBuildFile` items; a package reference alone is insufficient.
See [Distributing contracts](/smithy-dotnet/guides/distributing-contracts/)
for both configurations.

Declare Maven dependencies in `smithy-build.json` under `maven.dependencies`;
see [Maven dependencies](/smithy-dotnet/reference/maven-dependencies/) for
where they resolve from. There is no `SmithyMavenDependency` MSBuild item. The
build files of all referenced contracts are merged, and the same artifact at two
versions fails the build.
