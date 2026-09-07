# Changelog

All notable changes to NSmithy are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and NSmithy aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

> **Preview.** NSmithy is in preview — expect some API changes before 1.0.
> See [Protocol Status](https://thomaslaich.github.io/smithy-dotnet/protocols/status/)
> for supported surfaces, maturity, and official conformance coverage.

## [Unreleased]

## [0.10.0]

This release adds MCP tools and prompts backed by generated service handlers,
completes the applicable official HTTP protocol conformance suites, and simplifies
the generated C#. It also makes codec construction consistent and removes the
untyped schema API. Applications using low-level runtime APIs should review the
migration notes below.

### Added

- **MCP tools and prompts.** The new `NSmithy.Server.Mcp` package exposes generated
  unary service operations as MCP tools, with typed handlers, strict JSON codecs,
  validation, modeled errors, and Smithy-derived annotations. Service and operation
  `smithy.ai#prompts` traits produce MCP prompts with modeled arguments. The
  restJson1 example includes an MCP stdio mode. (#165, #168, #169)
- **Service catalogs for custom integrations.** Generated service definitions expose
  registered operation handlers and JSON Schema metadata for non-streaming inputs
  and outputs, allowing custom hosts to reuse the same handlers. (#166, #168)

### Fixed

- **Official HTTP protocol conformance gaps.** restJson1, simpleRestJson, and
  rpcv2Cbor now pass every applicable official request and response case on their
  client and server surfaces; restJson1 also passes all applicable malformed-request
  cases. AWS JSON 1.1, AWS Query, EC2 Query, and restXml pass every applicable
  official client request and response case. See [Protocol Status](https://thomaslaich.github.io/smithy-dotnet/protocols/status/)
  for coverage by protocol and surface. (#157, #158, #161, #162)

### Changed

- **BREAKING: consistent codec factories.** Replace the removed static codec entry
  points with `JsonCodecFactory`, `XmlCodecFactory`, `CborCodecFactory`, or
  `ProtoCodecFactory`. For example, construct a JSON codec with
  `JsonCodecFactory.Default.FromSchema(schema)`. Custom factories implement
  `ICodecFactory`, or `IProjectionCodecFactory` when projection support is needed;
  construction options now use `CodecFactoryOptions`. (#159)
- **BREAKING: typed schema APIs.** Removed the untyped/object schema tier, including
  `GetObject`/`SetObject`, object-based collection and enum/union accessors, and
  `Schemas.GetMembers`. Custom schema integrations should use typed schema visitors
  and member APIs, including `PartialSchemaVisitor`, `IBuilderMemberSchema`, and
  `ITypedTargetMemberSchema<TValue>.TypedTarget`. Structure projections now snapshot
  their selected members. Regenerate models with the matching codegen version when
  upgrading runtime packages. (#167)
- **BREAKING: explicit server runtime registration for manual hosting.** Generated
  aggregate handler registration calls `AddSmithyServer()` automatically.
  Applications registering individual operation handlers must call it themselves.
  Direct callers of `SmithyAspNetCoreHost.DispatchAsync` must supply the
  `SmithyServerRuntime` as the first argument. Existing application runtime
  registrations are preserved. (#170)
- **Earlier errors for unsupported HTTP bindings.** Unsupported binding kinds and
  non-integer `@httpResponseCode` members now fail during operation protocol
  construction, before the first request. (#167)
- **More readable generated C#.** Model and framework references use short names
  and collected imports where unambiguous, with `global::` qualification for name
  collisions. (#171)

### Packages

All packages are prepared for publication at `0.10.0`, including the new
`NSmithy.Server.Mcp` package. Codegen JARs and NuGet packages must use the same
release version.

## [0.9.0]

Adds generated fakes, AWS Query and EC2 Query clients, and broader AWS endpoint,
credential, and authentication support.

### Added

- **Fake handlers and clients.** Enable `SmithyGenerateFakes` (or `generateFakes`
  in codegen settings) to generate `Fake{Service}Handler` and `Fake{Service}Client`.
  They match inputs against modeled `@examples`, return canned responses or modeled
  errors, and fall back to deterministic placeholder values. Operation methods are
  virtual; fake client paginators yield a single page. Register fake handlers with
  `Add{Service}Handler<T>()`. (#147, #148, #150)
- **AWS Query and EC2 Query clients.** The new `NSmithy.Protocols.AwsQuery` package
  supports both protocols and passes their applicable official client conformance
  cases. (#155)
- **AWS endpoints and credentials.** `NSmithy.Aws` adds regional endpoint resolution
  across AWS partitions; environment, shared-profile, IAM Identity Center/SSO, and
  IMDSv2 credential providers; a default provider chain; credential caching; and
  SigV4 presigning. (#152, #155)
- **Modeled client behavior.** Generated clients support operation host prefixes,
  per-operation auth overrides, endpoint-provided auth narrowing, modeled HTTP
  version preferences, and a default NSmithy user agent. Host-prefix injection and
  the user agent are configurable through `SmithyClientConfig`. (#152)

### Changed

- **BREAKING: auth scheme interfaces.** Replace `ISmithyAuthScheme.CreateInterceptor`
  with `IdentityResolver` and `Signer`, implementing `ISmithyIdentityResolver` and
  `ISmithySigner` for custom schemes. `SmithyAuthSchemeContext`, `ISmithyAuthHandler`,
  `HeaderAuthInterceptor`, and `QueryParameterAuthInterceptor` are removed.
  `SmithyClientRuntime`'s auth map now contains `ISmithyAuthScheme` values. (#152)
- **BREAKING: protocol HTTP preferences.** Replace `IProtocol.RequiresHttp2` with
  `HttpVersionPreference`, which supports HTTP/1.1, HTTP/2, and HTTP/3 plus downgrade
  policy. Custom protocols using the default continue to use HTTP/1.1. (#152)
- **Lower codec and client overhead.** Reduced processing time and allocations for
  JSON, CBOR, Proto, modeled errors, and client calls without changing the wire
  format. (#143, #146, #149, #151, #154)

## [0.8.1]

### Fixed

- **Generated template projects build correctly.** Fixed the obsolete server mapper
  call, missing `NSmithy.MSBuild` reference in servers created without `--contracts`,
  and obsolete gRPC client constructor arguments. (#142)

## [0.8.0]

Adds server-side model validation and structured errors for malformed requests.

### Added

- **Modeled constraint validation.** Generated servers enforce `@length`, `@range`,
  `@pattern`, `@required`, and enum values before invoking handlers. Clients receive
  typed `NSmithy.Core.Validation.ValidationException` errors containing the member
  path and failed constraint. (#131)
- **Structured request errors.** Malformed values produce 400 `SerializationException`
  responses, unsupported request media types produce 415 `UnsupportedMediaTypeException`,
  and unacceptable response media types produce 406 `NotAcceptableException`.
  Generated restJson1 servers pass all 655 applicable official malformed-request
  cases. (#135)
- **Legacy `@enum` validation.** Servers validate values on string shapes carrying
  the deprecated trait. `@internal` values are accepted but omitted from rejection
  messages. (#131, #135)
- **Model documentation in IntelliSense.** Smithy documentation traits become XML
  comments on generated models, clients, servers, and errors. (#129)

### Changed

- **BREAKING: schema codec APIs.** Runtime schema member access now uses typed
  visitors. Top-level default materialization is aligned across JSON, CBOR, XML,
  and Proto codecs. (#134)
- **BREAKING: server mapper methods.** Use the single service mapper with protocol
  flags instead of protocol-specific methods: `app.MapWeatherServiceRpcV2Cbor()`
  becomes `app.MapWeatherService()`. Mapping conflicting routes is rejected. (#128)
- **BREAKING: REST codec factory argument.** `RestServiceProtocol` now takes
  `Func<WireReadMode, IRestBodyCodecFactory>` as its first constructor argument,
  allowing strict server reads and permissive client reads. (#135)
- **BREAKING: map key schemas.** `Schemas.Map` accepts a key schema, defaulting to
  `Schemas.String`. Replace `IMapSchema.TypedKeyMember` with `IMapSchema.KeyMember`.
  Enum-keyed maps generate `string` keys, while server validation enforces the
  modeled enum values. (#134, #135)
- **BREAKING: legacy enum generation.** String shapes with deprecated `@enum` traits
  no longer generate enum types; they map to `string` and retain server-side value
  validation. (#131)
- **Faster JSON codecs.** Reduced serialization and deserialization time and
  allocations without changing wire output. (#136)
- **Smithy 1.73.0.** Updated the bundled CLI and Smithy dependencies. `NSmithy.MSBuild`
  grows by roughly 20 MB with the bundled JRE 25; custom Java codegen integrations
  must account for the plugin's bytecode target moving from Java 17 to 21. (#139)
- **Clearer model errors.** Unsupported schemas, gRPC stream wrappers, and other
  unsupported constructs produce diagnostics naming the shape and reason. (#124–#127)

### Fixed

- **Defaults on explicit null.** Codecs now apply a member's modeled `@default`
  when its input value is explicitly null. (#136)

## [0.7.0]

Adds event streaming across rpcv2Cbor and restJson1.

### Added

- **`NSmithy.EventStream`.** A standalone library for `vnd.amazon.eventstream`
  message framing. (#94)
- **rpcv2Cbor event streaming.** Supports event-stream operations and their initial
  request and response messages. (#106, #108)
- **restJson1 streaming.** Adds streaming support to the protocol. (#110)

### Changed

- **BREAKING: protocol interfaces.** Client and server interfaces are separate;
  custom protocol implementations own their streaming framing. (#93)
- **BREAKING: streaming operation signatures.** Streaming operations now use
  `Task<TOutput>(TInput)`, with the event stream carried inside the input/output
  structure alongside any initial fields. This applies to gRPC as well. (#93, #108)
- **Bundled codegen dependencies.** `NSmithy.MSBuild` includes the codegen
  dependencies, so clean-machine builds need no locally built codegen JAR. (#105)

### Fixed

- **rpcv2Cbor example.** Corrected the runnable example. (#111)
- **Prerelease templates.** Scaffolded projects reference the actual published
  prerelease version. (#109)

## [0.6.0]

### Added

- **`DebugInterceptor`.** Logs typed input/output, each transport attempt's request
  and response, and body bytes as hex. Enable it in the rpcv2Cbor example client
  with `--debug`. (#100)
- **rpcv2Cbor templates.** `dotnet new` accepts `--protocol rpcv2Cbor`. (#97)
- **Expanded rpcv2Cbor example.** Demonstrates resources, pagination, modeled errors,
  and retries with the Weather service. (#98, #99)

### Fixed

- **Client template setup.** Corrected project setup and stale template references
  in the quick-start flow. (#97)

## [0.5.0]

Adds client observability, pagination, operation timeouts, and more capable retries.

### Added

- **OpenTelemetry instrumentation.** Client operations emit spans and metrics;
  the restJson1 example demonstrates an observability setup. (#86, #90)
- **Generated paginators.** `@paginated` operations expose `IAsyncEnumerable`
  paginator methods on generated clients. (#89)
- **Per-operation endpoint and auth selection.** Resolution runs for each operation
  rather than once per client. (#87)
- **Operation timeout.** `OperationTimeout` covers the entire execution, including
  retries. (#85)

### Changed

- **BREAKING: HTTP body APIs.** `SmithyHttpBody.Empty`, `Bytes`, and `Streaming`
  replace separate buffered/streaming request and response fields. Streaming bodies
  preserve their content length. (#79)
- **BREAKING: retry and interceptor APIs.** Custom retry strategies now return an
  `ISmithyRetrySession` from `Begin()`. `IClientInterceptor.OnAfterExecution` receives
  an additional `Exception?` argument. Direct runtime callers use
  `InvokeAsync(binding, input, ct)`. (#82)
- **Standard retries.** `SmithyStandardRetryStrategy` supports jittered exponential
  backoff, a shared retry quota, and `Retry-After`. Transport failures and modeled
  `@retryable` errors participate in retry classification; interceptors observe
  failures as well as successes. (#82)

### Fixed

- **Release packages build on clean machines.** `NSmithy.MSBuild` now references
  the published codegen version instead of an unpublished `-SNAPSHOT` JAR. (#95)
- **Streaming response cleanup.** The client runtime disposes abandoned streaming
  response bodies. (#84)
- **Client configuration isolation.** Client construction takes a copy of
  caller-supplied configuration. (#83)

## [0.4.0]

Adds AWS JSON, SigV4 authentication, and bidirectional gRPC streaming.

### Added

- **AWS JSON clients.** The new `NSmithy.Protocols.AwsJson` package supports AWS JSON
  services. (#60)
- **AWS authentication.** The new `NSmithy.Aws` package supports SigV4 signing and
  generated auth configuration. An AWS LocalStack example demonstrates usage. (#61)
- **Bidirectional gRPC streams.** Generated clients and ASP.NET Core servers support
  bidirectional event streams, with a `Grpc.Net` interoperability example. (#62)
- **Client interception and retries.** Generated clients use `SmithyClientRuntime`
  with interceptor hooks, auth resolution, and configurable retries. (#65–#67)

### Changed

- **Client middleware removed.** Migrate custom middleware to client interceptors.
  (#69)

## [0.3.0]

Adds native gRPC and clients that can use any protocol declared by a service.

### Added

- **Native gRPC.** New packages `NSmithy.Codecs.Proto` and `NSmithy.Protocols.Grpc`
  support HTTP/2 gRPC calls, modeled errors, and ASP.NET Core server mapping without
  `protoc`, `Grpc.Tools`, or `Grpc.Net.Client`. `Map{Service}Grpc` can coexist with
  REST mappings for services declaring both protocols. (#58)
- **Protocol selection on clients.** A single `{Service}Client` accepts an endpoint,
  `HttpClient`, or invoker, with an optional protocol defaulting to the service's
  primary declared protocol. (#58)
- **Opt-in dependency injection.** Set `SmithyGenerateDependencyInjection=true`
  (or `generateDependencyInjection` in codegen settings) to generate
  `Add{Service}Client(...)`. It configures HTTP/2 when required and only adds the
  `Microsoft.Extensions.Http` dependency when enabled. (#58)

### Changed

- **Protocol construction.** Instantiate protocols, such as `new GrpcProtocol()`,
  instead of using `.Instance`. `IProtocol` now exposes `RequiresHttp2`. (#58)
- **`SmithyClientOptions` removed.** Pass `middleware` and `idempotencyTokenProvider`
  directly to the generated client constructor. (#58)
- **Low-level HTTP APIs.** Request compression and content-MD5 handling move to
  `NSmithy.Http/SmithyRequestModifiers`. Custom operation protocols use
  `RequiresErrorDiscriminator` and `SupportsHttpStatusErrorFallback` for error
  dispatch. (#58)

## [0.2.0]

### Added

- **rpcv2Cbor servers.** Generated ASP.NET Core servers handle CBOR requests,
  responses, and modeled errors at
  `POST /service/{Service}/operation/{Operation}`. (#54)
- **REST server responses.** Generated servers honor `@http(code)`, output header,
  payload, and document bindings, plus modeled-error headers and status codes. (#55)

### Changed

- **Server-only generation by default.** `NSmithy.Server.AspNetCore` sets
  `SmithyGenerateServer=true` and `SmithyGenerateClient=false`. Server projects no
  longer require `NSmithy.Client` or a manual setting to disable client generation.
  (#53)

## [0.1.0]

First tagged release, superseding `0.1.0-preview.*`. Generate typed C# clients,
ASP.NET Core server stubs, and shared models from Smithy during `dotnet build`,
without a separate codegen step or a Java installation.

### Added

- **Project templates.** `NSmithy.Templates` provides `nsmithy-server`,
  `nsmithy-client`, and `nsmithy-contracts` templates.
- **REST clients and servers.** Supports `alloy#simpleRestJson` and
  `aws.protocols#restJson1`.
- **Additional client protocols.** Supports `aws.protocols#restXml` and
  `smithy.protocols#rpcv2Cbor` clients.
- **Experimental gRPC.** Supports `.proto` emission, clients, and ASP.NET Core
  servers for `alloy.proto#grpc`.

### Packages

- **Runtime and build integration:** `NSmithy.Core`, `NSmithy.Http`, `NSmithy.MSBuild`.
- **Client and server:** `NSmithy.Client`, `NSmithy.Server.AspNetCore`,
  `NSmithy.Server.AspNetCore.Docs`.
- **Codecs:** `NSmithy.Codecs.Json`, `NSmithy.Codecs.Cbor`, `NSmithy.Codecs.Xml`.
- **Protocols:** `NSmithy.Protocols.Rest`, `NSmithy.Protocols.RestJson`,
  `NSmithy.Protocols.RestXml`, `NSmithy.Protocols.RpcV2Cbor`.
- **Tooling:** `NSmithy.Templates`, `dotnet-nsmithy`.

[Unreleased]: https://github.com/thomaslaich/smithy-dotnet/compare/v0.10.0...HEAD
[0.10.0]: https://github.com/thomaslaich/smithy-dotnet/compare/v0.9.0...v0.10.0
[0.9.0]: https://github.com/thomaslaich/smithy-dotnet/compare/v0.8.1...v0.9.0
[0.8.1]: https://github.com/thomaslaich/smithy-dotnet/compare/v0.8.0...v0.8.1
[0.8.0]: https://github.com/thomaslaich/smithy-dotnet/compare/v0.7.0...v0.8.0
[0.7.0]: https://github.com/thomaslaich/smithy-dotnet/compare/v0.6.0...v0.7.0
[0.6.0]: https://github.com/thomaslaich/smithy-dotnet/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/thomaslaich/smithy-dotnet/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/thomaslaich/smithy-dotnet/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/thomaslaich/smithy-dotnet/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/thomaslaich/smithy-dotnet/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/thomaslaich/smithy-dotnet/releases/tag/v0.1.0
