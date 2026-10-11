---
title: Smithy CLI
description: How NSmithy finds the Smithy CLI, and how to provide your own.
---

Code generation runs the [Smithy CLI](https://smithy.io/2.0/guides/smithy-cli/index.html).
NSmithy looks for it in this order:

1. `SmithyCliPath`, when set.
2. The `NSmithy.SmithyCli` package, when the project references it.
3. `smithy` on `PATH`.

Before generating, NSmithy checks that the CLI's version equals `SmithyVersion`
(currently `1.73.0`).

## The NSmithy.SmithyCli package

`NSmithy.SmithyCli` contains the Smithy CLI with its Java runtime for macOS and
Linux on x64 and arm64, and Windows on x64. Nothing else needs to be installed. The
package version is the Smithy version, so upgrading NSmithy reuses the package
already in the NuGet cache.

The NSmithy templates add it to every project they create. To add it yourself,
reference it in each project that generates code:

```xml title="MyService.Server.csproj"
<ItemGroup>
  <PackageReference Include="NSmithy.SmithyCli" Version="1.73.0" />
</ItemGroup>
```

It is a development dependency, so it never becomes a dependency of a package
you publish. To add it to every project at once, put the reference in
`Directory.Build.props`, or with central package management use a global
package reference in `Directory.Packages.props`:

```xml title="Directory.Packages.props"
<ItemGroup>
  <GlobalPackageReference Include="NSmithy.SmithyCli" Version="1.73.0" />
</ItemGroup>
```

The package is restored with the rest of the project, so builds after
`dotnet restore` need no network access for the CLI. Private feeds that mirror
nuget.org need to include it.

## Providing your own CLI

Leave out `NSmithy.SmithyCli` to use a Smithy CLI you install yourself, for
example from a package manager, a dev container, or a Nix environment. NSmithy
then runs `smithy` from `PATH`.

With [pixi](https://pixi.sh), add the conda-forge
[`smithy`](https://anaconda.org/channels/conda-forge/packages/smithy/overview)
package pinned to `SmithyVersion`, and build inside the environment
(`pixi shell` or `pixi run dotnet build`):

```toml title="pixi.toml"
[dependencies]
smithy = "==1.73.0"
```

With Homebrew:

```shell
brew tap smithy-lang/tap
brew install smithy-cli
```

To use a CLI that is not on `PATH`, set `SmithyCliPath`:

```xml title="Directory.Build.props"
<PropertyGroup>
  <SmithyCliPath>/opt/smithy/bin/smithy</SmithyCliPath>
</PropertyGroup>
```

`SmithyCliPath` takes precedence over the package, so it also overrides
`NSmithy.SmithyCli` for a single build:

```shell
dotnet build -p:SmithyCliPath=/opt/smithy/bin/smithy
```

NSmithy runs a CLI it did not provide as is and never changes its file
permissions, so it can live on a read-only path such as the Nix store.

## Version check

The generator plugins are built against one Smithy version, so NSmithy requires
a CLI of exactly that version. For a CLI from `PATH` or `SmithyCliPath`, it runs
`smithy --version` before generating. The check only runs on builds that
regenerate code. A mismatch fails the build:

```text
error NSMITHYCLIVERSION: NSmithy: Smithy CLI 1.71.0 (smithy) does not match
SmithyVersion (1.73.0). Use Smithy CLI 1.73.0, or set SmithyCliVersionCheck to
false to skip this check.
```

When no CLI can be run, the build fails with `NSMITHYCLIMISSING`.

To use a different CLI version anyway, turn the check off:

```xml title="Directory.Build.props"
<PropertyGroup>
  <SmithyCliVersionCheck>false</SmithyCliVersionCheck>
</PropertyGroup>
```

The plugins require a CLI that runs Java 21 or later, which every Smithy CLI
since 1.73.0 bundles.
