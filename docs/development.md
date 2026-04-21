# Development

This guide covers building and testing the repository locally.

## Environment Setup

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

## Common Tasks

All day-to-day tasks are defined as [just](https://just.systems) recipes. Run
`just` with no arguments to list them.

| Recipe | What it does |
|---|---|
| `just restore` | Restore NuGet packages |
| `just build` | Build in Release configuration |
| `just test` | Run the test suite |
| `just fmt` | Format all sources (C#, Nix, YAML, Justfile) |
| `just check-format` | Verify formatting (used in CI) |
| `just pack` | Pack NuGet packages to `artifacts/packages` |
| `just ci` | Full CI sequence: restore → check-format → build → test → pack |

## Pack Local Packages

```bash
just pack
```

The generated `.nupkg` files are written to `artifacts/packages`.

### Consuming Local Packages

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

See [docs/releasing.md](releasing.md).
