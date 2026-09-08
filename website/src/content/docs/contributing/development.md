---
title: Development
description: How to build and test the NSmithy repository locally.
---

## Environment setup

The repository uses [devenv](https://devenv.sh) to provide a reproducible
development environment with all required tools (Smithy CLI, JDK, .NET SDKs,
formatters). See `devenv.nix` and `devenv.yaml` at the repo root for the full
definition.

The recommended way to activate the environment is via
[direnv](https://direnv.net). After installing direnv and running
`direnv allow` once in the repo root, the environment activates automatically
whenever you enter the directory.

To enter the shell manually instead:

```bash
devenv shell
```

## Common tasks

All day-to-day tasks are defined as [just](https://just.systems) recipes. Run
`just` with no arguments to list them.

| Recipe | What it does |
|---|---|
| `just restore` | Restore NuGet packages |
| `just codegen` | Build the Java generators and stage the bundled Maven repository |
| `just build` | Build in Release configuration (runs `codegen` and `restore` first) |
| `just test` | Run the test suite |
| `just clean` | Delete all build output, including the Smithy plugin cache under `obj/` |
| `just fmt` | Format C#, Java, Nix, YAML, and the justfile |
| `just check-format` | Verify formatting (used in CI) |
| `just pack` | Pack NuGet packages to `artifacts/packages` |
| `just docs` | Start the documentation dev server |
| `just ci` | Check formatting, build, test, pack, rebuild examples, and run AOT and template smoke tests |

Run `just build` before testing: conformance projects need the staged generator
JARs. Examples consume packed packages, so use `just pack` and
`just refresh-examples` after runtime or generator changes.

## Pack local packages

```bash
just pack
```

The generated `.nupkg` files are written to `artifacts/packages`.

### Consuming local packages

Add a `NuGet.config` next to the consumer project to make the local feed
available alongside nuget.org:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="smithy-net-local" value="../../artifacts/packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

Adjust the relative path to match your project layout.

## Releasing

See [Releasing](/smithy-dotnet/contributing/releasing/).
