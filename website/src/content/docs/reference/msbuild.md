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
| `SmithyService` | — | Smithy shape ID of the service to generate (e.g. `example.hello#HelloService`). Required when synthesizing a `smithy-build.json` from local or referenced model sources. |
| `SmithyBaseNamespace` | — | Prefix prepended to generated C# namespaces. For example, `Acme` maps `example.library` to `Acme.Example.Library`. It does not filter model shapes. |
| `SmithyGenerateServer` | `true` | Emit server stub types. Set to `false` in client-only projects. |
| `SmithyGenerateClient` | `true` | Emit client types. Set to `false` in server-only projects. |
| `SmithyGenerateDependencyInjection` | `false` | Generate DI registrations (flows into `smithy-build.json` as the `csharp-codegen` `generateDependencyInjection` setting). HTTP services get the `Add{Service}Client` IHttpClientFactory extension (requires `Microsoft.Extensions.Http` or the `Microsoft.AspNetCore.App` shared framework); `bote#kafkaJson` services get `Add{Service}Client`, `Add{Service}EventPublisher`, and `Add{Service}*Consumer` registrations (requires `NSmithy.Messaging.Kafka`). See [Dependency Injection](/smithy-dotnet/guides/client-configuration/dependency-injection/) and [Kafka JSON](/smithy-dotnet/protocols/bote-kafka-json/#dependency-injection). |
| `SmithyGenerateFakes` | `false` | Generate `Fake{Service}Handler` with the server surface and `Fake{Service}Client` with the client surface (flows into `smithy-build.json` as the `csharp-codegen` `generateFakes` setting). The fakes return each operation's first non-error `@examples` output when present, otherwise deterministic placeholder values synthesized from the model. See [Fake Handlers](/smithy-dotnet/servers/fake-handlers/) and [Fake Clients](/smithy-dotnet/guides/client-configuration/fake-clients/). |
| `SmithyBuildFile` | `$(MSBuildProjectDirectory)/smithy-build.json` | Path to the Smithy build configuration file. When absent and the project has local `model/**/*.smithy` files or referenced `SmithySource` items, NSmithy synthesizes one under `obj/`. |
| `SmithyProjection` | `source` | Smithy build projection to use. |
| `SmithyPlugin` | `csharp-codegen` | Smithy build plugin name. |
| `SmithyBuildOutputPath` | `$(IntermediateOutputPath)Smithy/` | Root directory for all Smithy build output. |
| `SmithyStampFile` | `$(SmithyBuildOutputPath)NSmithy.Generated.stamp` | Incremental build stamp file. Smithy codegen is skipped when inputs have not changed since this file was last written. |
| `SmithyEmitGeneratedFiles` | `false` | Show generated `.g.cs` files in IDE project views when `true`. |
| `SmithyCliPath` | bundled CLI | Smithy CLI executable. Set this to override the bundled executable. |

`NSmithy.Bote` additionally defines `SmithyGenerateAsyncApi` (default `false`),
`BoteVersion`, and `NSmithyBoteVersion`. Most applications should set only
`SmithyGenerateAsyncApi`; the version properties exist for advanced development
and release scenarios.

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
| `SmithySources` | `$(MSBuildProjectDirectory)/model` | Directory containing local `.smithy` source files to generate from or publish. |
| `SmithyMavenGroupId` | — | Maven `groupId` for the emitted JAR (e.g. `io.github.acme`). When set, `dotnet pack` produces a JAR alongside the `.nupkg`. |
| `SmithyMavenArtifactId` | — | Maven `artifactId` for the emitted JAR (e.g. `my-service-contracts`). Required when `SmithyMavenGroupId` is set. |

## Items

| Item | Description |
| --- | --- |
| `SmithySource` | `.smithy` files to include in the synthesized `smithy-build.json`. Populated automatically from the local `SmithySources` directory and from a `ProjectReference` to a project with `SmithyPublish=true`, or added manually for advanced cases. |
| `SmithyMavenDependency` | Maven coordinates of Smithy model JARs needed by the Smithy CLI (e.g. `com.disneystreaming.alloy:alloy-core:0.3.38`). These are written into the synthesized `smithy-build.json` and, in publishing projects, into the NuGet package for downstream consumers. |
| `SmithyMavenRepository` | Maven repository URI made available to the Smithy CLI. Extension packages use this to expose bundled, offline artifacts. |
| `SmithyCodegenInput` | Files that participate in incremental codegen invalidation, typically an extension package's bundled JARs. |
| `SmithyBuildPlugin` | Additional smithy-build plugin. The item identity is the plugin name; `Service` and `SettingsJson` metadata configure it. |
| `SmithyContractsBuildFile` | Contract build configuration used when synthesizing a consumer build file. Collected automatically from a contracts project reference; set explicitly for packaged models. |

## Contract inputs

A contracts project reference supplies model sources and its `smithy-build.json`
automatically. NuGet packages require explicit `SmithySource` and
`SmithyContractsBuildFile` items; a package reference alone is insufficient.
See [Distributing contracts](/smithy-dotnet/guides/distributing-contracts/)
for both configurations.

Declare Maven dependencies in `smithy-build.json` under `maven.dependencies`
or add `SmithyMavenDependency` items. Build-file synthesis reads one contract
build configuration; use an explicit build file when combining models with
separate configurations.

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
