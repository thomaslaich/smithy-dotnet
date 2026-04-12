# Smithy.NET

Smithy.NET is an early-stage .NET toolkit for generating C# code and runtime clients from Smithy models.

Current status: repository scaffolding and planning are in progress. See `docs/planning/roadmap.md` for the implementation roadmap.

## Development

This repository uses Nix and devenv as the supported development environment.

Prerequisites:

- Install Nix. Determinate Nix is recommended.
- Install devenv: `nix profile add nixpkgs#devenv`

Enter the shell:

```bash
devenv shell
```

Common commands:

```bash
just restore       # dotnet restore
just fmt           # format the repo with treefmt
just check-format  # verify treefmt formatting
just build         # release build
just test          # release tests
just pack          # create NuGet packages under artifacts/packages
just ci            # restore, format check, build, test, pack
```
