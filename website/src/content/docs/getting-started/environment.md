---
title: Environment Setup
description: Set up the Smithy CLI, JDK, and .NET for NSmithy.
---

NSmithy's MSBuild integration invokes the Smithy CLI during `dotnet build`. The
CLI is a JVM tool, so a JDK must be available alongside the .NET SDK. The
recommended approach is a managed devshell that provides all three automatically.

## Using pixi

[pixi](https://pixi.sh) manages the Smithy CLI, JDK, and .NET SDK in a
reproducible conda-forge environment.

```bash
pixi init
pixi add smithy openjdk dotnet
pixi shell
```

Once the shell is active, `smithy` and `dotnet` are both on `PATH` and
`JAVA_HOME` is set correctly.

For a full working example see the
[simple-rest-json example](https://github.com/thomaslaich/smithy-dotnet/tree/main/examples/simple-rest-json).

## Using devenv

[devenv](https://devenv.sh) is a Nix-based alternative that achieves the same
result via a `devenv.nix` file. This repository itself uses devenv — see
`devenv.nix` and `devenv.yaml` at the repo root for a working reference.

For a standalone minimal example see
[smithy-dotnet-minimal-devenv](https://github.com/thomaslaich/smithy-dotnet-minimal-devenv).

## Other Options

Any environment that puts `smithy` and `dotnet` on `PATH` and sets `JAVA_HOME`
will work — Docker devcontainers, Nix flakes, or a manual JDK install. If
`smithy` is not on `PATH`, set `SmithyCliPath` in your `.csproj` to the
explicit executable path.
