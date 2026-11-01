# Build Configuration

How a project's `smithy-build.json` and its MSBuild properties combine into the
build that the Smithy CLI runs.

## Overview

Two files describe a code-generation build:

- **The project file says what the project builds**: the service, whether it
  gets the client, the server, documentation, OpenAPI, or a `.proto` file.
- **`smithy-build.json` says how**: the model's sources, Maven dependencies and
  repositories, suppressions, projections, every other plugin setting, and any
  plugins the project runs besides NSmithy's.

On every build, NSmithy merges the two into one effective build file under
`obj/` and runs the Smithy CLI on it. A project without its own
`smithy-build.json` gets the same treatment with an empty one. There is one
code path, and every MSBuild property means the same thing in every project.

## Goals

- One rule for where each setting lives, with no mode that changes what a
  property does.
- A project with a contracts reference generates code from a few properties and
  no `smithy-build.json` of its own.
- Every plugin setting is available, including ones added to a plugin later,
  without a matching MSBuild property.
- The project's own `smithy-build.json` stays a plain Smithy file: the Smithy
  IDE tooling reads it, and a contracts project publishes it.
- A setting configured in two places fails the build with a message that names
  the fix; nothing is silently ignored.
- The build that ran is inspectable as a file.

## Inputs

| Input | Source |
| --- | --- |
| Project build file | `$(SmithyBuildFile)`, `smithy-build.json` at the project root by default. Optional. |
| Contract build files | `SmithyContractsBuildFile` items, collected from contracts project references or set for packaged contracts. |
| Contract sources | `SmithySource` items, collected the same way. |
| Generation properties | `SmithyService`, `SmithyGenerateClient`, `SmithyGenerateServer`, `SmithyGenerateDocs`, `SmithyOpenApiProtocol`, `SmithyGrpc`, and `SmithyProjection`. |
| Contract coordinates | `SmithyMavenGroupId`, `SmithyMavenArtifactId`, and `PackageVersion` of a contracts project. |

## The effective build file

NSmithy writes the effective build file to
`$(IntermediateOutputPath)smithy-build.json` and passes it to `smithy build`.
It is assembled in this order:

1. **Model settings.** Everything in the project build file is kept as written:
   `sources`, `imports`, `maven`, `suppressions`, `projections`, `plugins`, and
   any other key. Relative `sources` and `imports` are resolved against the
   project build file's directory.
2. **Contracts.** Each contract build file contributes its Maven dependencies,
   repositories, and suppressions; its plugins are not carried over.
   Repositories keep all their settings, credentials and proxies included; the
   first entry for a URL wins.
   `SmithySource` items are added to `sources` as absolute paths.
3. **Repositories.** When neither the project nor a contract lists a Maven
   repository, Maven Central is listed, as the plain Smithy CLI assumes it. The
   [bundled repository](#bundled-maven-repository) is searched before any
   listed one.
4. **Settings from properties.** Each enabled plugin gets the settings its
   properties set, in the projection `SmithyProjection` selects (see
   [Plugin settings](#plugin-settings)).
5. **Plugin artifacts.** Each of NSmithy's plugins that appears anywhere in the
   file gets its Maven artifact added to the dependencies: the NSmithy plugins
   at the NSmithy version, `docgen` and `openapi` at `SmithyVersion`.

Dependencies are combined by coordinate. The same artifact at two versions
fails the build and names both sources.

The file is rewritten only when its content changes, and it is an input of the
generation step, so a property change regenerates and an unchanged project
skips the CLI.

## Plugin settings

A setting comes from exactly one place. Properties set the keys below;
`smithy-build.json` sets every other key of the same plugin. A key set both
ways fails the build.

| Plugin | Enabled by | Keys from properties |
| --- | --- | --- |
| `csharp-codegen` | `SmithyService` | `service` ← `SmithyService`; `generateClient` ← `SmithyGenerateClient` and `generateServer` ← `SmithyGenerateServer` when `false` |
| `docgen` | `SmithyGenerateDocs` | `service` ← `SmithyService` |
| `openapi` | `SmithyOpenApiProtocol` | `service` ← `SmithyService`, `protocol` ← `SmithyOpenApiProtocol` |
| `proto-codegen` | `SmithyGrpc` | `service` ← `SmithyService` |
| `contracts-jar` | `SmithyMavenGroupId` with `SmithyPublish` | `groupId` ← `SmithyMavenGroupId`, `artifactId` ← `SmithyMavenArtifactId`, `version` ← `PackageVersion` |

Some keys have defaults that apply only when the file does not set them:
`openapi` writes integer shapes as `integer` (`useIntegerType`), and
`proto-codegen` takes `baseNamespace` from `csharp-codegen` and suffixes C#
namespaces with `Grpc`.

The settings are written into `projections.<SmithyProjection>.plugins`, added
to the settings Smithy would use for that projection: the projection's own
when it defines the plugin, which replace top-level ones entirely, or else the
file's top-level `plugins` entry. The top-level entry stays as written, so
other projections keep inheriting it. A projection other than `source` must
exist in the project build file.

`SmithyService` is the switch for code generation from properties. A project
without it adds no code generation plugins from properties. Such a project either only builds
and validates its model, as a contracts project does, or configures NSmithy's
plugins entirely in `smithy-build.json`, for example one `csharp-codegen` per
projection to generate several services. `docgen`, `openapi`, and
`proto-codegen` take their service from `SmithyService`; enabling one of them
without it fails the build.

For example, a client that generates from a contracts project and also wants
fakes and a namespace prefix:

```xml
<PropertyGroup>
  <SmithyService>example.hello#HelloService</SmithyService>
  <SmithyGenerateServer>false</SmithyGenerateServer>
</PropertyGroup>
```

```json
{
  "version": "1.0",
  "plugins": {
    "csharp-codegen": { "generateFakes": true, "baseNamespace": "Acme" }
  }
}
```

The effective `csharp-codegen` settings are `service`, `generateServer: false`,
`generateFakes`, and `baseNamespace`.

## Collecting output

C# files are compiled from every projection's `csharp-codegen` output.
`SmithyGenerateClient` and `SmithyGenerateServer` set to `false` also leave the
corresponding files out of compilation, whichever projection produced them.
`.proto` files are registered the same way. In Web SDK projects, documentation
and OpenAPI output in `SmithyProjection` is copied to `wwwroot/docs/` and
`wwwroot/openapi.json` whenever the build produces it.

## Bundled Maven repository

The effective build file names artifacts; it does not decide where they come
from. With `SmithyUseBundledMavenRepo` on, NSmithy points the CLI at the Maven
repository inside `NSmithy.MSBuild` first, so its plugins and the bundled trait
packages resolve locally and other artifacts fall through to the listed
repositories. See [Maven dependencies](../website/src/content/docs/reference/maven-dependencies.md).

## Contracts projects

A contracts project (`SmithyPublish=true`) publishes its own `smithy-build.json`
in the NuGet package, never the effective build file. Its consumers therefore
receive the model's settings and none of the contracts project's generation
settings. Without `SmithyService`, a contracts project's build validates the
model and generates no code.

With `SmithyMavenGroupId` set, the effective build file also runs NSmithy's
`contracts-jar` plugin, which packages the projection's sources as a Maven JAR
and POM. `dotnet pack` copies them next to the `.nupkg`. The plugin is an
ordinary Smithy build plugin, so a project outside .NET produces the same
artifact from its own `smithy-build.json`. The POM declares the Maven artifacts
that provide the shapes and traits the model references, derived from the
model rather than copied from `maven.dependencies`. Publishing the files is
left to Maven tooling.

## Diagnostics

| Code | Condition |
| --- | --- |
| `NSMITHYPLUGIN` | A plugin key is set both by a property and in a build file. |
| `NSMITHYSERVICE` | `SmithyGenerateDocs`, `SmithyOpenApiProtocol`, or `SmithyGrpc` is set without `SmithyService`. |
| `NSMITHYPROJECTION` | `SmithyProjection` names a projection the project build file does not define. |
| `NSMITHYDEPENDENCY` | Two inputs declare the same Maven artifact at different versions. |
| `NSMITHYMAVEN` | `SmithyMavenGroupId` is set without `SmithyMavenArtifactId`, a version, or a project build file, or `dotnet pack` finds no JAR to copy. |

Each message names the property or file to change.

## Alternatives Considered

### The project's file runs as is

The Smithy Gradle plugin's split: plugins and their settings live in
`smithy-build.json`, and the build tool selects the file and projection and
supplies dependencies. It never generates or merges the file. It is simple and
explicit, but every server and client project then needs its own
`smithy-build.json`, including those that only consume a contracts reference.
Offering it next to synthesis produces two modes in which the same property
either works or is ignored.

### A property for every plugin setting

Mirroring each `csharp-codegen` setting as an MSBuild property keeps a project
without its own `smithy-build.json` fully configurable, but every new plugin
setting then needs a property, the reference doubles, and a project that
configures plugins in its file has to know which properties stop applying.
Properties cover the decisions about what a project builds; settings that tune
the output live where Smithy keeps them.

### Ownership per plugin

Letting either the properties or the file configure a whole plugin, never
both, avoids merging plugin settings, but makes a single file-only setting such
as `generateFakes` move the entire plugin, service included, into
`smithy-build.json`. Ownership per key keeps the service in the project file
in that case.

### Merging with precedence

Letting a property and the file set the same key, with one winning, keeps both
habits working but makes the outcome depend on a precedence rule a reader has
to look up. Failing on overlap keeps a single owner per setting.

### Everything in the project file

Expressing sources, dependencies, and projections as MSBuild items would remove
`smithy-build.json` from consumer projects entirely. The Smithy IDE tooling and
the published contracts both need a real `smithy-build.json`, so the model's
settings stay in it.

## Tradeoffs

- **Tuning a project without a build file needs one.** A client that generates
  from a contracts reference and wants fakes or a namespace prefix adds a small
  `smithy-build.json` with only those plugin settings.
- **The effective file differs from the one in the repository.** The IDE sees
  the project build file and the CLI runs the effective one. They differ only
  in the settings from properties, the contract inputs, and the plugin
  artifacts, and the effective file is on disk under `obj/` for inspection.
- **Maven Central is assumed when nothing else is listed.** Matching the plain
  Smithy CLI means an unbundled dependency resolves without configuration, at
  the cost of a network request the first time. Listing any repository,
  including a mirror or a `file://` directory, replaces it.
