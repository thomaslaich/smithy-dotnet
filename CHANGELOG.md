# Changelog

All notable changes to NSmithy are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and NSmithy aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

> **Preview.** NSmithy is in preview — expect some API changes before 1.0.
> Protocol implementations are not yet on par with the
> [Smithy reference implementations](https://github.com/smithy-lang/smithy).

## [Unreleased]

### Added

- **`DebugInterceptor`.** A built-in client interceptor that logs the typed
  input and output, each transport attempt's request and response, and a hex
  dump of the body bytes. Useful for inspecting what a protocol puts on the
  wire. (#100)

## [0.5.0]

This release adds observability to the client runtime, generated paginators,
per-operation endpoint and auth-scheme resolution, and a reworked retry
strategy. It also fixes the release packaging bug that made previous releases
unusable on machines without a locally built codegen JAR.

### Added

- **OpenTelemetry instrumentation.** The client runtime emits spans and metrics
  for operation execution, and the rest-json1 example wires up an end-to-end
  observability stack. (#86, #90)
- **Generated paginators.** `@paginated` operations get `IAsyncEnumerable`
  paginator methods on the generated client. (#89)
- **Per-operation endpoint resolution and auth scheme selection.** Endpoint and
  auth-scheme resolution now run per operation instead of per client. (#87)
- **Operation timeout.** `OperationTimeout` applies a deadline over the whole
  operation execution, including retries. (#85)
- **Explicit HTTP body model.** Request and response bodies are represented by
  an explicit model in the HTTP layer. (#79)

### Changed

- **Retry overhaul.** The retry strategy was reworked. (#82)
- **Docs reworked.** Protocol pages share a single usage example, design and
  protocol docs were updated for 0.4.0 accuracy, and the landing page and docs
  copy were toned down. (#78, #80, #81, #92)

### Fixed

- **Released packages referenced an unpublished codegen JAR.** The packed
  `NSmithy.MSBuild` build files shipped the `-SNAPSHOT` dev default for
  `SmithyCSharpCodegenVersion`, so released packages injected a Maven dependency
  on a codegen version that only exists in a dev `~/.m2` — code generation
  failed on any clean machine following the quickstart. The release version is
  now substituted into the packed files, and packing fails if the default
  drifts. (#95)
- **Streaming response bodies.** Abandoned streaming response bodies are now
  disposed by the client runtime. (#84)
- **Client configuration.** Caller-supplied config is copied at client
  construction instead of being referenced. (#83)

### Packages

All packages are published to NuGet at `0.5.0`.

## [0.4.0]

This release expands protocol and authentication coverage, adds generated
bidirectional gRPC event streaming, and moves the generated client stack onto the
new runtime pipeline with interceptors, auth-scheme resolution, retry, and
precomputed operation bindings.

### Added

- **AWS JSON client protocol.** `NSmithy.Protocols.AwsJson` adds client-side
  support for AWS JSON protocol services. (#60)
- **AWS authentication.** `NSmithy.Aws` adds SigV4 request signing, generated
  auth-scheme wiring, and an AWS LocalStack example for exercising real AWS-style
  authentication locally. (#61)
- **gRPC event streaming.** Generated gRPC clients and ASP.NET Core servers now
  support bidirectional event streams, with an interop example that can compare
  NSmithy's native gRPC transport against `Grpc.Net`. (#62)
- **Client runtime pipeline.** Generated clients now route calls through
  `SmithyClientRuntime`, interceptor hooks, auth resolution, and runtime-owned
  retry strategy configuration. (#65, #66, #67)
- **Analyzer coverage.** Java codegen builds now use Error Prone, and .NET builds
  enable globalization analyzers. (#63)

### Changed

- **Middleware replaced by interceptors.** The old client middleware abstraction
  has been removed in favor of Smithy-style interceptors. (#69)
- **Client operation dispatch simplified.** The generated client no longer wraps
  calls in `SmithyOperationInvoker`; operation bindings and modeled error
  deserializers are precomputed at construction time. (#70, #73, #74, #76)
- **Docs reworked.** The README, landing page, docs theme, and client
  configuration guides were reorganized around the current client architecture.
  (#68, #72, #75)

### Packages

All packages are published to NuGet at `0.4.0`.

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

[Unreleased]: https://github.com/thomaslaich/smithy-dotnet/compare/v0.5.0...HEAD
[0.5.0]: https://github.com/thomaslaich/smithy-dotnet/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/thomaslaich/smithy-dotnet/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/thomaslaich/smithy-dotnet/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/thomaslaich/smithy-dotnet/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/thomaslaich/smithy-dotnet/releases/tag/v0.1.0
