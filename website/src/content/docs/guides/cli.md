---
title: dotnet nsmithy CLI
description: Pack Smithy contracts into Maven JARs and publish them to any Maven registry — no Java tooling required.
---

`dotnet nsmithy` is a .NET global tool for teams whose contract authors are on .NET
and don't want to maintain a Gradle or Maven build just to publish Smithy models.
It creates a spec-compliant Maven JAR from your `.smithy` files and pushes it to
any Maven-compatible registry over HTTP.

## Installation

```bash
dotnet tool install -g NSmithy.Tool
```

For a repo-scoped install (recommended — teammates get it via `dotnet tool restore`):

```bash
dotnet tool install NSmithy.Tool --local
```

The NuGet package is named `NSmithy.Tool` to stay consistent with the rest of the
NSmithy package family. Once installed, the command is `dotnet nsmithy`.

## Contracts project

Create a minimal `.csproj` next to your `.smithy` files. The version, Maven group,
artifact ID, and source directory are all read from here automatically — no flags
required on the command line.

```
my-contracts/
├── my-contracts.csproj
└── model/
    └── hello.smithy
```

```xml title="my-contracts.csproj"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <VersionPrefix>1.0.0</VersionPrefix>

    <SmithyMavenGroupId>com.example</SmithyMavenGroupId>
    <SmithyMavenArtifactId>my-contracts</SmithyMavenArtifactId>
    <SmithySources>model</SmithySources>
  </PropertyGroup>
</Project>
```

| Property | Description |
|---|---|
| `SmithyMavenGroupId` | Maven `groupId` (e.g. `com.example`) |
| `SmithyMavenArtifactId` | Maven `artifactId` (e.g. `my-contracts`) |
| `SmithySources` | Path to the `.smithy` source directory, relative to the `.csproj` |
| `VersionPrefix` / `VersionSuffix` | Standard MSBuild version properties — compose the Maven version |

## pack

Creates a Maven JAR containing the `.smithy` files and a `META-INF/smithy/manifest`,
plus a POM and MD5/SHA1 checksums ready for upload.

```bash
# Run from the contracts project directory — reads everything from the .csproj
dotnet nsmithy pack

# Override individual values
dotnet nsmithy pack --version 1.1.0-beta.1 --output dist/
```

**Options**

| Option | Short | Description |
|---|---|---|
| `--project` | `-p` | Path to the `.csproj`. Auto-discovered in the current directory. |
| `--sources` | `-s` | `.smithy` source directory. Overrides `SmithySources`. |
| `--group` | `-g` | Maven `groupId`. Overrides `SmithyMavenGroupId`. |
| `--artifact` | `-a` | Maven `artifactId`. Overrides `SmithyMavenArtifactId`. |
| `--version` | `-v` | Maven version. Overrides the project version. |
| `--output` | `-o` | Output directory for the generated files. Defaults to the current directory. |

**Output**

```
my-contracts-1.0.0.jar
my-contracts-1.0.0.jar.md5
my-contracts-1.0.0.jar.sha1
my-contracts-1.0.0.pom
my-contracts-1.0.0.pom.md5
my-contracts-1.0.0.pom.sha1
```

## push

Uploads a packed artifact (JAR + POM + checksums) to a Maven registry via HTTP PUT.
Reads Maven coordinates from the `.csproj` the same way `pack` does.

```bash
dotnet nsmithy push --registry https://maven.pkg.github.com/YOUR_ORG/YOUR_REPO
```

Credentials are resolved in this order:
1. `--username` / `--token` flags
2. `MAVEN_USERNAME` / `MAVEN_TOKEN` environment variables
3. `GITHUB_ACTOR` / `GITHUB_TOKEN` environment variables

**Options**

| Option | Short | Description |
|---|---|---|
| `--registry` | `-r` | Maven registry base URL **(required)** |
| `--project` | `-p` | Path to the `.csproj`. Auto-discovered in the current directory. |
| `--group` | `-g` | Maven `groupId`. Overrides `SmithyMavenGroupId`. |
| `--artifact` | `-a` | Maven `artifactId`. Overrides `SmithyMavenArtifactId`. |
| `--version` | `-v` | Maven version. Overrides the project version. |
| `--username` | `-u` | Registry username. |
| `--token` | `-t` | Registry token / password. |
| `directory` | | Directory containing the packed files. Defaults to the current directory. |

## Full workflow

```bash
# 1. Pack
dotnet nsmithy pack --output dist/

# 2. Push (GitHub Packages — token from gh CLI)
export GITHUB_TOKEN=$(gh auth token)
export GITHUB_ACTOR=$(gh api user --jq .login)
dotnet nsmithy push dist/ --registry https://maven.pkg.github.com/YOUR_ORG/YOUR_REPO
```

### CI (GitHub Actions)

```yaml
- name: Pack contracts
  run: dotnet nsmithy pack --output dist/

- name: Push contracts
  env:
    GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
  run: >
    dotnet nsmithy push dist/
    --registry https://maven.pkg.github.com/${{ github.repository }}
```

`GITHUB_ACTOR` is injected automatically by Actions; you only need `GITHUB_TOKEN`.

