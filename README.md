_Work in Progress: NSmithy is a proof of concept. The current implementation
demonstrates that Smithy models can drive idiomatic C# clients and ASP.NET Core
servers end-to-end, but the protocol implementations are not yet on par with
the [Smithy reference implementations](https://github.com/smithy-lang/smithy)._

# NSmithy

**Documentation: [thomaslaich.github.io/smithy-dotnet](https://thomaslaich.github.io/smithy-dotnet/)**

NSmithy is a preview-stage .NET toolkit that turns a [Smithy](https://smithy.io)
model into idiomatic C# at build time. From a single contract you get the same
model types, typed clients, and server scaffolding that any other Smithy
language would produce. NSmithy aims to fully integrate into your MSBuild workflow,
in order to make code generation as seamless as possible.

## Features

- **Code generation from MSBuild**: Generates C# types, clients, and ASP.NET Core server scaffolding from Smithy models during `dotnet build`.
- **Typed protocol-aware clients**: Supports `alloy#simpleRestJson`, `aws.protocols#restJson1`, `aws.protocols#restXml`, and `smithy.protocols#rpcv2Cbor`.
- **ASP.NET Core server surfaces**: Implements Smithy services as ASP.NET Core endpoints with minimal boilerplate.
- **Conformance**: Protocols are tested against the official Smithy/AWS and alloy protocol test suites.

## Quick Start

The fastest way to try NSmithy is with the [simple-rest-json](https://github.com/thomaslaich/smithy-dotnet/tree/main/examples/simple-rest-json) example. It shows a minimal project using NSmithy and [pixi](https://pixi.sh) for environment management.

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
