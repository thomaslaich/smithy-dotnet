# Changelog

All notable changes to NSmithy are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and NSmithy aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

> **Preview.** NSmithy is in preview — expect some API changes before 1.0.
> Protocol implementations are not yet on par with the
> [Smithy reference implementations](https://github.com/smithy-lang/smithy).

## [Unreleased]

## [0.1.0]

First tagged release. NSmithy turns a [Smithy](https://smithy.io) model into
idiomatic C# at build time — typed clients, ASP.NET Core minimal-API server
stubs, and shared model types, generated as part of `dotnet build`. No separate
codegen step, and no Java or JRE required. Earlier `0.1.0-preview.*` builds are
superseded.

### Getting started

```bash
dotnet new install NSmithy.Templates
dotnet new nsmithy-server   # or: nsmithy-client, nsmithy-contracts
dotnet build
```

See the [Quick Start guide](https://thomaslaich.github.io/smithy-dotnet/getting-started/quick-start/)
for the full walkthrough.

### Protocol support

| Protocol | Generated surfaces | Stage |
| --- | --- | --- |
| `alloy#simpleRestJson` | client + ASP.NET Core server | Preview — most complete |
| `aws.protocols#restJson1` | client + ASP.NET Core server | Preview |
| `aws.protocols#restXml` | client only | Early preview |
| `smithy.protocols#rpcv2Cbor` | client only | Early preview |
| `alloy.proto#grpc` | `.proto` emission + gRPC client + ASP.NET Core gRPC server | Experimental |

We recommend `aws.protocols#restJson1` for new projects. `restXml`
and `rpcv2Cbor` are client-only for now. Full breakdown on the
[Protocol Status](https://thomaslaich.github.io/smithy-dotnet/protocols/status/) page.

### Known limitations

NSmithy is preview-stage and has rough edges — please read
[Known Limitations](https://thomaslaich.github.io/smithy-dotnet/reference/known-limitations/)
before filing issues, and report anything not already listed there.

### Packages

All published to NuGet at `0.1.0`:

- **Runtime / codegen:** `NSmithy.Core`, `NSmithy.Http`, `NSmithy.MSBuild`
- **Client / server:** `NSmithy.Client`, `NSmithy.Server.AspNetCore`, `NSmithy.Server.AspNetCore.Docs`
- **Codecs:** `NSmithy.Codecs.Json`, `NSmithy.Codecs.Cbor`, `NSmithy.Codecs.Xml`
- **Protocols:** `NSmithy.Protocols.Rest`, `NSmithy.Protocols.RestJson`, `NSmithy.Protocols.RestXml`, `NSmithy.Protocols.RpcV2Cbor`
- **Tooling:** `NSmithy.Templates` (project templates), `dotnet-nsmithy` (CLI tool)

[Unreleased]: https://github.com/thomaslaich/smithy-dotnet/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/thomaslaich/smithy-dotnet/releases/tag/v0.1.0
