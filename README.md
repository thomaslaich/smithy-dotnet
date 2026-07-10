[![CI](https://github.com/thomaslaich/smithy-dotnet/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/thomaslaich/smithy-dotnet/actions/workflows/ci.yml)
[![Docs](https://github.com/thomaslaich/smithy-dotnet/actions/workflows/docs.yml/badge.svg?branch=main)](https://thomaslaich.github.io/smithy-dotnet/)
[![NuGet](https://img.shields.io/nuget/v/NSmithy.Client)](https://www.nuget.org/packages/NSmithy.Client)
[![.NET 10](https://img.shields.io/badge/.NET-net10.0-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/github/license/thomaslaich/smithy-dotnet)](https://github.com/thomaslaich/smithy-dotnet/blob/main/LICENSE)
[![Smithy CLI](https://img.shields.io/badge/smithy--cli-1.71.0-orange)](https://github.com/smithy-lang/smithy/releases/tag/1.71.0)

> **Preview:** NSmithy is in preview; expect some API changes before 1.0. Protocol implementations are not yet on par with the [Smithy reference implementations](https://github.com/smithy-lang/smithy).

# NSmithy

**[Docs](https://thomaslaich.github.io/smithy-dotnet/)** · **[Examples](examples/README.md)** · **[Design Docs](designs/README.md)** · **[smithy.io](https://smithy.io)**

NSmithy is a .NET toolkit that turns a [Smithy](https://smithy.io) model into idiomatic C# at build time. From a single contract you get typed clients, server scaffolding, and shared model types, fully integrated into your MSBuild workflow.

## Features

- **Contract-first**: The Smithy model is the source of truth. NSmithy generates C# model types, typed clients, and ASP.NET Core server handlers from it.
- **Protocol-agnostic**: The same model can target multiple protocols and wire formats; switching protocols requires no changes to client or server code.
- **Part of the Smithy ecosystem**: A .NET service built with NSmithy can be called from clients generated for Java, TypeScript, Python, Go, Rust, Swift, and more, and vice versa.
- **Smithy-native architecture**: Follows Smithy's official [code generator guidance](https://smithy.io/2.0/guides/building-codegen/index.html), with a clear boundary between generated code and runtime libraries.
- **Protocol support**: REST JSON, REST XML, AWS JSON, RPC v2 CBOR, and native gRPC.
- **Streaming support**: Event streaming, bidirectional streaming, and streaming blob payloads.
- **Conformance-tested**: Tested against official Smithy, AWS, and alloy conformance suites.

## Development

The recommended way to work on this repo is with [Nix](https://nixos.org/) and [devenv](https://devenv.sh/).

1. **Install Nix** (recommended: [Determinate Nix](https://determinate.systems/nix/)) and [devenv](https://devenv.sh/).
2. **Optionally install [direnv](https://direnv.net/)** to activate the dev environment automatically when entering the directory (`direnv allow`). Without it, run `devenv shell` manually.
3. **Use the `just` recipes** to build, test, and package:

   ```bash
   just          # list all available recipes
   just build    # build the codegen JAR and .NET solution
   just test     # run the test suite
   just fmt      # format all code
   just docs     # start the documentation dev server
   just ci       # run the full CI pipeline locally
   ```

## Related Projects

- **[Smithy](https://smithy.io)**: The IDL and protocol framework NSmithy is built on.
- **[smithy4s](https://disneystreaming.github.io/smithy4s/)**: Scala codegen from Smithy models and the main inspiration for NSmithy.
- **[alloy](https://github.com/disneystreaming/alloy)**: Smithy extensions used by NSmithy for `simpleRestJson` and gRPC.
- **[smithy-go](https://github.com/smithy-lang/smithy-go)** / **[smithy-typescript](https://github.com/smithy-lang/smithy-typescript)**: Official Smithy codegen plugins for Go and TypeScript.
- **[TypeSpec](https://typespec.io)**: Microsoft's alternative API description language, with first-party .NET emitters.
