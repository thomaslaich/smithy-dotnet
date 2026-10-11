# NSmithy

[![CI](https://github.com/thomaslaich/smithy-dotnet/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/thomaslaich/smithy-dotnet/actions/workflows/ci.yml)
[![Docs](https://github.com/thomaslaich/smithy-dotnet/actions/workflows/docs.yml/badge.svg?branch=main)](https://thomaslaich.github.io/smithy-dotnet/)
[![NuGet](https://img.shields.io/nuget/v/NSmithy.Client)](https://www.nuget.org/packages/NSmithy.Client)
[![.NET 10](https://img.shields.io/badge/.NET-net10.0-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/github/license/thomaslaich/smithy-dotnet)](https://github.com/thomaslaich/smithy-dotnet/blob/main/LICENSE)
[![Smithy CLI](https://img.shields.io/badge/smithy--cli-1.73.0-orange)](https://github.com/smithy-lang/smithy/releases/tag/1.73.0)

> **Preview:** NSmithy is in preview; expect some API changes before 1.0.

---

**[Docs](https://thomaslaich.github.io/smithy-dotnet/)** · **[Examples](examples/README.md)** · **[Design Docs](designs/README.md)** · **[smithy.io](https://smithy.io)**

NSmithy generates C# for .NET from [Smithy](https://smithy.io) models. Smithy is the interface definition language AWS uses for its SDKs: a model describes a service's operations, data types, and errors once, independent of programming language and protocol. Smithy has code generators for Java, TypeScript, Go, Rust, Python, Kotlin, and Scala, so a .NET service and clients in those languages can share one model.

NSmithy generates typed clients, ASP.NET Core server stubs, and model types from a Smithy model during `dotnet build`, with no separate codegen step. A client and a server generated from the same model agree on routes, serialization, and errors. It supports restJson1, rpcv2Cbor, gRPC, and the AWS protocols; see [protocol status](https://thomaslaich.github.io/smithy-dotnet/protocols/status/).

## Quick start

With the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and the [Smithy CLI](https://thomaslaich.github.io/smithy-dotnet/reference/smithy-cli/#installing-the-smithy-cli) installed:

```bash
dotnet new install NSmithy.Templates
dotnet new nsmithy-contracts -n HelloWorld.Contracts
dotnet new nsmithy-server -n HelloWorld.Server --contracts-project HelloWorld.Contracts
dotnet run --project HelloWorld.Server
```

Pass `--smithy-cli-package` to the templates to get the Smithy CLI from NuGet instead. The [quick start](https://thomaslaich.github.io/smithy-dotnet/getting-started/quick-start/) adds a client and walks through the generated code.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for the development environment and build recipes.

## Related Projects

- [Smithy](https://smithy.io), the IDL NSmithy is built on.
- [smithy4s](https://disneystreaming.github.io/smithy4s/), the main inspiration for NSmithy.
- [alloy](https://github.com/disneystreaming/alloy), used for `simpleRestJson` and gRPC.
- [smithy-go](https://github.com/smithy-lang/smithy-go) and [smithy-typescript](https://github.com/smithy-lang/smithy-typescript), the official generators for Go and TypeScript.
- [TypeSpec](https://typespec.io), Microsoft's alternative with first-party .NET emitters.
