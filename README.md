<p align="center">
   <img src="website/public/brand/nsmithy_logo_1.png" alt="NSmithy logo" width="320" />
</p>

<p align="center">
   <a href="https://github.com/thomaslaich/smithy-dotnet/actions/workflows/ci.yml">
      <img src="https://github.com/thomaslaich/smithy-dotnet/actions/workflows/ci.yml/badge.svg?branch=main" alt="CI" />
   </a>
   <a href="https://thomaslaich.github.io/smithy-dotnet/">
      <img src="https://github.com/thomaslaich/smithy-dotnet/actions/workflows/docs.yml/badge.svg?branch=main" alt="Docs" />
   </a>
   <a href="https://www.nuget.org/packages/NSmithy.Client">
      <img src="https://img.shields.io/nuget/v/NSmithy.Client" alt="NuGet" />
   </a>
   <a href="https://dotnet.microsoft.com/">
      <img src="https://img.shields.io/badge/.NET-net10.0-512BD4" alt=".NET 10" />
   </a>
   <a href="LICENSE">
      <img src="https://img.shields.io/github/license/thomaslaich/smithy-dotnet" alt="License" />
   </a>
</p>

_Work in Progress: NSmithy is a proof of concept. The current implementation
demonstrates that Smithy models can drive idiomatic C# clients and ASP.NET Core
servers end-to-end, but the protocol implementations are not yet on par with
the [Smithy reference implementations](https://github.com/smithy-lang/smithy)._

# NSmithy

**Documentation: [thomaslaich.github.io/smithy-dotnet](https://thomaslaich.github.io/smithy-dotnet/)**

**Design docs: [designs/README.md](designs/README.md)**

NSmithy is a preview-stage .NET toolkit that turns a [Smithy](https://smithy.io)
model into idiomatic C# at build time. From a single contract you get the same
model types, typed clients, and server scaffolding that any other Smithy
language would produce. NSmithy aims to fully integrate into your MSBuild workflow,
in order to make code generation as seamless as possible.

## Features

- **MSBuild integration**: Generate idiomatic C# models, typed clients, and ASP.NET Core server stubs from Smithy models during `dotnet build`.
- **Client and server generation**: Turn a Smithy IDL contract into normal C# types and service surfaces that fit naturally into .NET projects.
- **Multiple protocol paths**: Support `alloy#simpleRestJson`, `aws.protocols#restJson1`, `aws.protocols#restXml`, and `smithy.protocols#rpcv2Cbor`.
- **Conformance-tested protocols**: Exercise protocol support against official Smithy, AWS, and alloy conformance suites.

## Development

The recommended way to work on this repo is with [Nix](https://nixos.org/) (preferably [Determinate Nix](https://determinate.systems/nix/)) and [devenv](https://devenv.sh/).

1. **Install Nix** (recommended: [Determinate Nix](https://determinate.systems/nix/)) and [devenv](https://devenv.sh/).
2. **Optionally install [direnv](https://direnv.net/)** to activate the dev environment automatically when entering the directory (`direnv allow`). Without it, run `devenv shell` manually.
3. **Use the `just` recipes** to build, test, and package:

   ```bash
   just          # list all available recipes
   just build    # build the codegen JAR and .NET solution
   just test     # run the test suite
   just fmt      # format all code
   just ci       # run the full CI pipeline locally
   ```
