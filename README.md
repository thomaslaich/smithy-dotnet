<p align="center">
   <img src="https://raw.githubusercontent.com/thomaslaich/smithy-dotnet/main/website/public/brand/nsmithy_logo_1.png" alt="NSmithy logo" width="320" />
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
   <a href="https://github.com/smithy-lang/smithy/releases/tag/1.71.0">
      <img src="https://img.shields.io/badge/smithy--cli-1.71.0-orange" alt="Smithy CLI" />
   </a>
</p>

> **Preview:** NSmithy is in preview — expect some API changes before 1.0. Protocol implementations are not yet on par with the [Smithy reference implementations](https://github.com/smithy-lang/smithy).

# NSmithy

**[Docs](https://thomaslaich.github.io/smithy-dotnet/)** · **[Design Docs](designs/README.md)** · **[smithy.io](https://smithy.io)**

NSmithy is a .NET toolkit that turns a [Smithy](https://smithy.io) model into idiomatic C# at build time. From a single contract you get typed clients, server scaffolding, and shared model types — fully integrated into your MSBuild workflow.

## Features

- **Contract-first**: The Smithy model is the source of truth: operations, shapes, errors, traits, and documentation live in one distributable contract. NSmithy generates C# model types, typed clients, and ASP.NET Core server handlers from it.
- **Protocol-agnostic**: The same model can target multiple protocols, serialization formats, and wire shapes without rewriting the service contract.
- **Language-agnostic interop**: Smithy has generators across many ecosystems. A service generated with NSmithy can share a contract with clients or servers generated for Java, TypeScript, Python, Go, Rust, Swift, and more.
- **Smithy-native architecture**: NSmithy follows Smithy's official [code generator guidance](https://smithy.io/2.0/guides/building-codegen/index.html): reusable shape generation, protocol-specific runtime packages, and a clear boundary between generated code and runtime libraries.
- **Protocol support**: Supports REST JSON, REST XML, AWS JSON, RPC v2 CBOR, and native gRPC.
- **Streaming support**: Supports unary operations, event streaming, bidirectional streaming, and streaming blob payloads across protocols.
- **Conformance-tested**: Protocol implementations are tested against official Smithy, AWS, and alloy conformance suites.

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

- **[Smithy](https://smithy.io)** — the IDL and protocol framework NSmithy is built on.
- **[smithy4s](https://disneystreaming.github.io/smithy4s/)** — the main inspiration for NSmithy; generates Scala code from Smithy models with similar goals, though with a more sophisticated typeclass-based codec architecture that cleanly separates schema interpretation from serialization.
- **[alloy](https://github.com/disneystreaming/alloy)** — Smithy extensions used by NSmithy for `simpleRestJson` and gRPC protocols.
- **[smithy-go](https://github.com/smithy-lang/smithy-go)** / **[smithy-typescript](https://github.com/smithy-lang/smithy-typescript)** — official Smithy codegen plugins for Go and TypeScript, which NSmithy draws inspiration from.
- **[TypeSpec](https://typespec.io)** — Microsoft's alternative API description language with similar goals. Compiles to OpenAPI, JSON Schema, Protobuf, and more; has first-party .NET emitters.
