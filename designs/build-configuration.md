# Build Configuration

How a project's `smithy-build.json` and its MSBuild properties combine into the
build that the Smithy CLI runs.

## Overview

Two files describe a code-generation build:

- **`smithy-build.json` describes the model**: its sources, the Maven
  dependencies and repositories it needs, suppressions, projections, and any
  plugins the project runs besides NSmithy's.
- **The project file describes what NSmithy generates**: MSBuild properties
  select and configure NSmithy's plugins (`csharp-codegen`, `docgen`, `openapi`,
  and the Protobuf plugin).

On every build, NSmithy merges the two into one effective build file under
`obj/` and runs the Smithy CLI on it. A project without its own
`smithy-build.json` gets the same treatment with an empty one. There is one
code path, and every MSBuild property means the same thing in every project.

## Goals

- One rule for where each setting lives, with no mode that changes what a
  property does.
- A project with a contracts reference generates code from a few properties and
  no `smithy-build.json` of its own.
- The project's own `smithy-build.json` stays a plain Smithy file: the Smithy
  IDE tooling reads it, and a contracts project publishes it.
- Misconfiguration fails the build with a message that names the fix, instead
  of producing no output.
- The build that ran is inspectable as a file.

## Inputs

| Input | Source |
| --- | --- |
| Project build file | `$(SmithyBuildFile)`, `smithy-build.json` at the project root by default. Optional. |
| Contract build files | `SmithyContractsBuildFile` items, collected from contracts project references or set for packaged contracts. |
| Contract sources | `SmithySource` items, collected the same way. |
| Generation properties | The MSBuild properties in the table below. |

## The effective build file

NSmithy writes the effective build file to
`$(IntermediateOutputPath)smithy-build.json` and passes it to `smithy build`.
It is assembled in this order:

1. **Model settings.** Everything in the project build file is kept as written:
   `sources`, `imports`, `maven`, `suppressions`, `projections`, `plugins`, and
   any other key. Relative paths are resolved against the project build file's
   directory.
2. **Contracts.** Each contract build file contributes its Maven dependencies,
   repositories, and suppressions. `SmithySource` items are added to `sources`
   as absolute paths.
3. **Repositories.** When neither the project nor a contract lists a Maven
   repository, Maven Central is listed, as the plain Smithy CLI assumes it. The
   [bundled repository](#bundled-maven-repository) is searched before any
   listed one.
4. **NSmithy's plugins.** Each enabled plugin is added with its settings, and
   its Maven artifact is added to the dependencies.

Dependencies are combined by coordinate. The same artifact at two versions
fails the build and names both sources.

The file is rewritten only when its content changes, and it is an input of the
generation step, so a property change regenerates and an unchanged project
skips the CLI.

## NSmithy's plugins

| Plugin | Enabled by | Settings from |
| --- | --- | --- |
| `csharp-codegen` | `SmithyService` | `service` ← `SmithyService`, `baseNamespace` ← `SmithyBaseNamespace`, `generateClient` ← `SmithyGenerateClient`, `generateServer` ← `SmithyGenerateServer`, `generateDependencyInjection` ← `SmithyGenerateDependencyInjection`, `generateFakes` ← `SmithyGenerateFakes` |
| `docgen` | `SmithyGenerateDocs` | `service` ← `SmithyService` |
| `openapi` | `SmithyOpenApiProtocol` | `service` ← `SmithyService`, `protocol` ← `SmithyOpenApiProtocol`, `useIntegerType` = `true` |
| `proto-codegen` | `SmithyGrpc` | `service` ← `SmithyService`, `baseNamespace` ← `SmithyBaseNamespace` |

`SmithyService` is the switch for code generation. A project without it, such
as a contracts project, builds and validates its model and generates nothing.
`docgen`, `openapi`, and `proto-codegen` require `SmithyService`; enabling one
without it fails the build.

The plugins are placed in the projection named by `SmithyProjection`. For the
default `source` projection that is the top-level `plugins` object; for any
other name it is `projections.<name>.plugins`, and that projection must exist
in the project build file. The steps that collect output (compiling `.g.cs`
files, copying docs and OpenAPI into `wwwroot/`, registering `.proto` files)
read from the same projection.

## Ownership

NSmithy's plugins are configured through properties only. If the project build
file or a contract build file configures `csharp-codegen`, `docgen`, `openapi`,
or `proto-codegen` in the selected projection, the build fails:

```text
error NSMITHYPLUGIN: smithy-build.json configures the docgen plugin, which
NSmithy configures from MSBuild properties. Remove it and set SmithyGenerateDocs
to true instead.
```

There is no precedence between the two sources, so a value never silently
loses. Plugins NSmithy does not own are kept and run as configured.

## Bundled Maven repository

The effective build file names artifacts; it does not decide where they come
from. With `SmithyUseBundledMavenRepo` on, NSmithy points the CLI at the Maven
repository inside `NSmithy.MSBuild` first, so its plugins and the bundled trait
packages resolve locally and other artifacts fall through to the listed
repositories. See [Maven dependencies](../website/src/content/docs/reference/maven-dependencies.md).

## Contracts projects

A contracts project (`SmithyPublish=true`) publishes its own `smithy-build.json`
in the NuGet package and the Maven JAR, never the effective build file. Its
consumers therefore receive the model's settings and none of the contracts
project's NSmithy plugins. Without `SmithyService`, a contracts project's build
validates the model and generates nothing.

## Diagnostics

| Code | Condition |
| --- | --- |
| `NSMITHYPLUGIN` | A build file configures a plugin NSmithy owns. |
| `NSMITHYSERVICE` | `docgen`, `openapi`, or `proto-codegen` is enabled without `SmithyService`. |
| `NSMITHYPROJECTION` | `SmithyProjection` names a projection the project build file does not define. |
| `NSMITHYDEPENDENCY` | Two inputs declare the same Maven artifact at different versions. |

Each message names the property or file to change.

## Alternatives Considered

### The project's file runs as is

The Smithy Gradle plugin's split: plugins and their settings live in
`smithy-build.json`, and the build tool selects the file and projection and
supplies dependencies. It never generates or merges the file. It is simple and
explicit, but every server and client project then needs its own
`smithy-build.json` with plugin settings, including those that only consume a
contracts reference. Offering it next to synthesis produces two modes in which
the same property either works or is ignored.

### Merging with precedence

Letting the project file and the properties both configure NSmithy's plugins,
with one winning per key, keeps both habits working but makes the outcome
depend on a precedence rule a reader has to look up. Failing on overlap keeps a
single owner per setting.

### Everything in the project file

Expressing sources, dependencies, and projections as MSBuild items would remove
`smithy-build.json` from consumer projects entirely. The Smithy IDE tooling and
the published contracts both need a real `smithy-build.json`, so the model's
settings stay in it.

## Tradeoffs

- **A plugin's settings come only from properties.** A `csharp-codegen` setting
  is usable only once it has an MSBuild property. The plugin's settings map one
  to one onto properties, and new settings get a property when they are added.
- **The effective file differs from the one in the repository.** The IDE sees
  the project build file and the CLI runs the effective one. They differ only
  in NSmithy's plugins and the contract inputs, and the effective file is on
  disk under `obj/` for inspection.
- **Maven Central is assumed when nothing else is listed.** Matching the plain
  Smithy CLI means an unbundled dependency resolves without configuration, at
  the cost of a network request the first time. Listing any repository,
  including a mirror or a `file://` directory, replaces it.
