---
title: Maven dependencies
description: Where code generation resolves Smithy plugins and trait packages, building offline, and resolving from Maven repositories instead.
---

The Smithy CLI resolves the plugins and trait packages a build uses as Maven
artifacts: the ones in the `maven.dependencies` of `smithy-build.json`, plus
NSmithy's code-generation plugin.

## The bundled repository

`NSmithy.MSBuild` contains a Maven repository with NSmithy's C# and Protobuf
plugins, the C# plugin's AWS integration, and the artifacts the templates and
examples use:

- `software.amazon.smithy`: `smithy-aws-traits`, `smithy-protocol-traits`,
  `smithy-waiters`, `smithy-openapi`, `smithy-docgen`, and their dependencies.
- `com.disneystreaming.alloy:alloy-core`.

During generation NSmithy points the CLI at this repository and at a Maven
cache under `obj/`, so these artifacts resolve from local files at the versions
the NSmithy release fixes.

Other dependencies resolve from the repositories `smithy-build.json` lists,
after the bundled repository. When neither the project nor a referenced
contract lists one, NSmithy lists Maven Central, as the plain Smithy CLI
assumes it:

```json title="smithy-build.json"
{
  "version": "1.0",
  "sources": ["model"],
  "maven": {
    "dependencies": ["software.amazon.smithy:smithy-aws-iam-traits:1.73.0"]
  }
}
```

Listing any repository, such as an internal mirror, replaces Maven Central.
NSmithy points the CLI at the bundled repository through `SMITHY_MAVEN_REPOS`,
which replaces a value set in your environment.

## Offline builds

A build needs network access for code generation only when it resolves an
artifact from a listed repository for the first time. With bundled
dependencies only, and with `NSmithy.SmithyCli` or a local Smithy CLI, every
build after `dotnet restore` runs offline.

To keep other dependencies offline, list a repository you control, such as an
internal mirror or a `file://` directory; Maven Central is then not used.

## Resolving from Maven repositories

Set `SmithyUseBundledMavenRepo` to `false` to resolve everything, NSmithy's
plugins included, the way the plain Smithy CLI does:

```xml title="Directory.Build.props"
<PropertyGroup>
  <SmithyUseBundledMavenRepo>false</SmithyUseBundledMavenRepo>
</PropertyGroup>
```

Artifacts then come from the repositories in `smithy-build.json` or
`SMITHY_MAVEN_REPOS`, or from Maven Central when neither lists one, and are
cached in your Maven cache (`~/.m2/repository`). NSmithy's plugins are
published to Maven Central as `io.github.thomaslaich.nsmithy:smithy-csharp-codegen`,
`smithy-csharp-aws-codegen`, and `smithy-proto-codegen`, at the NSmithy version. Use this to resolve through
a mirror, or to share one Maven cache across projects.
