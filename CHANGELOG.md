# Changelog

All notable changes to NSmithy are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and NSmithy aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

> **Preview.** NSmithy is in preview — expect some API changes before 1.0.
> Protocol implementations are not yet on par with the
> [Smithy reference implementations](https://github.com/smithy-lang/smithy).

## [Unreleased]

## [0.3.0]

Native gRPC arrives, and client construction is reworked so a single generated
`{Service}Client` can speak any protocol the service declares. gRPC now runs on
NSmithy's own proto3 codec and gRPC protocol over the shared HTTP transport — no
`protoc`, `Grpc.Tools`, or `Grpc.Net.Client` dependency.

### Added

- **Native gRPC.** Two new packages — `NSmithy.Codecs.Proto` (a schema-driven
  proto3 wire codec) and `NSmithy.Protocols.Grpc` (`GrpcProtocol`: 5-byte
  message framing, `application/grpc+proto`, the `grpc-status` trailer error
  model, and HTTP/2) — implement gRPC over the same `HttpClientTransport` as the
  REST and rpcv2Cbor protocols, with no `protoc` / `Grpc.Tools` / `Grpc.Net`
  dependency. Servers gain a native `Map{Service}Grpc` that coexists with the
  REST map for dual-protocol services. (#58)
- **Protocol-agnostic client construction.** Codegen now emits a single
  `{Service}Client` with `(endpoint, …)`, `(httpClient, …)`, and `(invoker, …)`
  constructors, each taking an optional `protocol` that defaults to the service's
  primary declared protocol. The same client speaks whichever `IProtocol` it is
  given, so a service may declare any combination of protocols. (#58)
- **Opt-in generated dependency injection.** Setting
  `SmithyGenerateDependencyInjection=true` generates an `Add{Service}Client(...)`
  extension (flowing through `smithy-build.json` as the `generateDependencyInjection`
  codegen setting). It is generation-gated, so the `Microsoft.Extensions.Http`
  dependency is only pulled in when enabled, and it configures HTTP/2 from the
  selected protocol. See the new
  [Dependency Injection](https://thomaslaich.github.io/smithy-dotnet/guides/dependency-injection/)
  guide. (#58)

### Changed

- **Protocols are instantiable.** `IProtocol` exposes `RequiresHttp2`, and
  protocols are now constructed (`new GrpcProtocol()`) rather than reached through
  static `.Instance` singletons. (#58)
- **`SmithyClientOptions` removed.** `middleware` and `idempotencyTokenProvider`
  are now first-class constructor parameters on the generated client. (#58)
- **Protocol-agnostic request mutations.** Compression and content-MD5 handling
  moved into `NSmithy.Http/SmithyRequestModifiers`, and error dispatch is unified
  through `IOperationProtocol.RequiresErrorDiscriminator` /
  `SupportsHttpStatusErrorFallback`. (#58)

### Protocol support

| Protocol | Generated surfaces | Stage |
| --- | --- | --- |
| `alloy#simpleRestJson` | client + ASP.NET Core server | Preview — most complete |
| `aws.protocols#restJson1` | client + ASP.NET Core server | Preview |
| `smithy.protocols#rpcv2Cbor` | client + ASP.NET Core server | Preview |
| `aws.protocols#restXml` | client only | Early preview |
| `alloy.proto#grpc` | `.proto` emission + native gRPC client + ASP.NET Core gRPC server | Experimental |

gRPC now runs on NSmithy's own proto codec and gRPC protocol; see the
[Protocol Status](https://thomaslaich.github.io/smithy-dotnet/protocols/status/)
page for current conformance numbers.

### Packages

All published to NuGet at `0.3.0`:

- **Runtime / codegen:** `NSmithy.Core`, `NSmithy.Http`, `NSmithy.MSBuild`
- **Client / server:** `NSmithy.Client`, `NSmithy.Server.AspNetCore`, `NSmithy.Server.AspNetCore.Docs`
- **Codecs:** `NSmithy.Codecs.Json`, `NSmithy.Codecs.Cbor`, `NSmithy.Codecs.Xml`, `NSmithy.Codecs.Proto`
- **Protocols:** `NSmithy.Protocols.Rest`, `NSmithy.Protocols.RestJson`, `NSmithy.Protocols.RestXml`, `NSmithy.Protocols.RpcV2Cbor`, `NSmithy.Protocols.Grpc`
- **Tooling:** `NSmithy.Templates` (project templates), `dotnet-nsmithy` (CLI tool)

`NSmithy.Codecs.Proto` and `NSmithy.Protocols.Grpc` are new in this release.

## [0.2.0]

Server-side protocol support takes a big step forward. `smithy.protocols#rpcv2Cbor`
gains a generated ASP.NET Core server, and REST servers learn to serialize
responses and modeled errors — bringing `aws.protocols#restJson1` and
`alloy#simpleRestJson` to full server-side conformance alongside their clients.

### Added

- **rpcv2Cbor server generation.** `NSmithy.Server.AspNetCore` now emits ASP.NET
  Core minimal-API servers for `smithy.protocols#rpcv2Cbor` services, routed at
  `POST /service/{Service}/operation/{Operation}`, with CBOR request
  deserialization plus response and modeled-error serialization. (#54)
- **REST server response handling.** Generated REST servers now honor the
  `@http(code)` success status, project outputs through their HTTP bindings
  (header / payload / document), and serialize modeled errors with the protocol's
  error-type header and HTTP status. `restJson1` and `simpleRestJson` now run the
  full set of applicable server conformance cases, not just clients. (#55)

### Changed

- **`NSmithy.Server.AspNetCore` defaults to server-only codegen.** The package
  now ships props setting `SmithyGenerateServer=true` / `SmithyGenerateClient=false`,
  so server-only projects no longer compile the generated client (which referenced
  `NSmithy.Client`) or need a manual `SmithyGenerateClient=false` workaround. (#53)

### Protocol support

| Protocol | Generated surfaces | Stage |
| --- | --- | --- |
| `alloy#simpleRestJson` | client + ASP.NET Core server | Preview — most complete |
| `aws.protocols#restJson1` | client + ASP.NET Core server | Preview |
| `smithy.protocols#rpcv2Cbor` | client + ASP.NET Core server | Preview |
| `aws.protocols#restXml` | client only | Early preview |
| `alloy.proto#grpc` | `.proto` emission + gRPC client + ASP.NET Core gRPC server | Experimental |

`rpcv2Cbor` now generates servers in addition to clients; see the
[Protocol Status](https://thomaslaich.github.io/smithy-dotnet/protocols/status/)
page for current conformance numbers.

### Packages

All published to NuGet at `0.2.0`:

- **Runtime / codegen:** `NSmithy.Core`, `NSmithy.Http`, `NSmithy.MSBuild`
- **Client / server:** `NSmithy.Client`, `NSmithy.Server.AspNetCore`, `NSmithy.Server.AspNetCore.Docs`
- **Codecs:** `NSmithy.Codecs.Json`, `NSmithy.Codecs.Cbor`, `NSmithy.Codecs.Xml`
- **Protocols:** `NSmithy.Protocols.Rest`, `NSmithy.Protocols.RestJson`, `NSmithy.Protocols.RestXml`, `NSmithy.Protocols.RpcV2Cbor`
- **Tooling:** `NSmithy.Templates` (project templates), `dotnet-nsmithy` (CLI tool)

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

[Unreleased]: https://github.com/thomaslaich/smithy-dotnet/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/thomaslaich/smithy-dotnet/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/thomaslaich/smithy-dotnet/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/thomaslaich/smithy-dotnet/releases/tag/v0.1.0
