# Contributing to NSmithy

Thanks for your interest in contributing. This file is a short pointer; the full
contributor documentation lives in the docs site.

## Getting started

The repository uses [Nix](https://nixos.org/) and [devenv](https://devenv.sh/)
for a reproducible development environment (Smithy CLI, JDK, .NET SDKs,
formatters). With [direnv](https://direnv.net/) the environment activates on
`cd` into the repo (`direnv allow`); otherwise run `devenv shell`.

Day-to-day tasks are [just](https://just.systems/) recipes:

```bash
just          # list all recipes
just build    # build the codegen JAR and .NET solution
just test     # run the test suite
just fmt      # format all sources
just ci       # run the full CI pipeline locally
```

## Documentation

- **[Development](https://thomaslaich.github.io/smithy-dotnet/contributing/development/)** — full environment setup, recipe reference, and local package consumption.
- **[Releasing](https://thomaslaich.github.io/smithy-dotnet/contributing/releasing/)** — how releases are cut.
- **[Roadmap](https://thomaslaich.github.io/smithy-dotnet/contributing/roadmap/)** — current direction and near-term priorities.

The sources for these pages are under
[`website/src/content/docs/contributing/`](website/src/content/docs/contributing/).
