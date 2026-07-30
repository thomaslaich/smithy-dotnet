---
title: Roadmap
description: Current direction and near-term priorities for NSmithy.
---

The architecture is settled: NSmithy uses the Smithy CLI for model assembly
and a Smithy Java plugin for generation, integrated into the .NET build
through `NSmithy.MSBuild`. This roadmap covers hardening and expanding that
baseline rather than revisiting it, guided by a few principles:

- Keep Smithy CLI as the model front end for assembly, validation, projections,
  and Maven dependency resolution.
- Keep the generated output and runtime idiomatic for .NET consumers.
- Prefer explicit preview boundaries over broad compatibility claims.
- Use protocol expansion to validate and strengthen the runtime seams that are
  already in place.

For what has already shipped, see the
[changelog](https://github.com/thomaslaich/smithy-dotnet/blob/main/CHANGELOG.md).
The priorities below are what remains.

## Near-Term Priorities

### 1. Expand AWS protocol coverage and AWS readiness

- Expand AWS protocol coverage beyond the initial AWS JSON client support,
  especially AWS Query and EC2 Query.
- Continue hardening `aws.protocols#restJson1`, `aws.protocols#restXml`, and
  `smithy.protocols#rpcv2Cbor` as preview surfaces.
- Mature AWS authentication beyond the early-preview SigV4 signing — endpoint
  resolution, profile/SSO/IMDS credential chains, presigning, and golden-vector
  coverage against AWS's SigV4 test suite.
- Grow the LocalStack integration coverage beyond the initial example into a
  broader suite that validates generated AWS clients against realistic protocol,
  signing, and endpoint behavior.
- Keep the scope driven by conformance and observed runtime behavior rather
  than by protocol checklists.

### 2. Move the client runtime to the target architecture

The core client runtime pipeline, standard retry, operation timeouts,
telemetry, and paginators have landed; the desired
end-state is documented in
[`designs/client-architecture.md`](https://github.com/thomaslaich/smithy-dotnet/blob/main/designs/client-architecture.md).
The remaining work closes the gaps:

- Splitting auth into scheme resolution, identity resolution, and signing;
  adding per-operation `@auth` overrides and identity caching/refresh.
- Adding per-operation endpoint resolution beyond the static resolver,
  including host labels and endpoint auth-scheme overrides.
- Setting a modeled/default User-Agent.
- Continuing to harden named client interceptors and the typed per-call
  execution context.

### 3. XML doc comments from Smithy documentation traits

Smithy's `@documentation` trait and `///` doc comments are not yet emitted as
C# XML doc comments (`/// <summary>…</summary>`) on generated types and members.
Adding this would improve the IDE experience for consumers of generated code —
hover documentation, parameter hints, and IntelliSense would reflect the
model's documentation rather than being empty.

### 4. Improve generator clarity and diagnostics

- Keep generated output predictable and easy to inspect.
- Improve unsupported-shape and unsupported-trait diagnostics.
- Continue simplifying generator internals where semantics are harder to follow
  than they need to be.
- Revisit the generated server mapping API so service mapping can be
  protocol-selectable, for example `MapFooService(protocols)`, while preserving
  protocol-specific internals and handling route conflicts explicitly.

### 5. Enforce constraint traits and modeled validation

Smithy's constraint traits — `@length`, `@pattern`, `@range`, `@uniqueItems`,
and server-side enforcement of `@required` — are not yet enforced. `@required`
is currently honored only for C# nullability, and the value constraints are
ignored entirely, so generated servers accept input the model forbids.

The focus is server-first: Smithy's contract is that constraint violations
produce a structured `smithy.framework#ValidationException` before the operation
handler runs. Client-side validation of outbound input is out of scope for now.

This work includes:

- Generating constraint checks from the model and rejecting violations with a
  structured `smithy.framework#ValidationException` before the handler runs.
- Enforcing `@required` on the server rather than only using it for C#
  nullability.
- Covering server behavior with Smithy's malformed-request conformance suites.
- Documenting the client-side non-goal as an explicit preview boundary.

### 6. Improve CBOR and XML codec performance through schema-compiled codecs

JSON already benefits from compiling codec state once from the schema so the
runtime can cache structural decisions such as dispatch and boxing behavior.
CBOR and XML should move in the same direction so runtime performance does not
depend on repeating more dynamic codec work in the hot path.

This work includes:

- Compiling CBOR codecs from schema once, using the same general approach
  already used for JSON.
- Compiling XML codecs from schema once where the shape model allows it.
- Caching the same kind of per-shape decisions that let the JSON path avoid
  unnecessary boxing and repeated dynamic dispatch.
- Keeping the generated codec path explicit enough that performance work does
  not make diagnostics and debuggability worse.

### 7. Harden streaming operations

NSmithy has two experimental event-streaming surfaces: native gRPC (client,
server, and bidirectional streaming) and `rpcv2Cbor` event streams over
`vnd.amazon.eventstream` message framing, sharing the `NSmithy.EventStream`
framing layer. The next step is to harden these paths and keep the abstractions
usable across additional streaming protocols.

This work includes:

- Adding end-to-end tests that cover backpressure, cancellation, errors, and
  stream completion behavior across both surfaces.
- Adding interop tests with `Grpc.Net` peers generated from the emitted `.proto`.
- Extending streaming support beyond event streams, especially streaming blob
  payloads.

### 8. Expand to async protocols

NSmithy's current protocol work is mostly request/response oriented. A separate
near-term goal is to validate that the runtime and generator model can also
support async protocol families cleanly.

This work includes:

- Exploring first-class support for Kafka-oriented messaging workflows.
- Exploring AMQP-based protocols and the runtime abstractions they require.
- Exploring Redis-oriented protocol patterns where Smithy models map cleanly to
  command and messaging semantics.
- Using these protocols to pressure-test the existing transport, codec, and
  client/server seams beyond HTTP-centric assumptions.

### 9. Support Smithy AI traits and MCP generation

Support Smithy's AI-oriented traits so that .NET and protocol artifacts can be
generated for tool-driven and agent-driven workflows, rather than treating the
traits as out-of-band metadata.

This work includes:

- Supporting relevant Smithy AI traits during model interpretation and codegen.
- Generating Model Context Protocol (MCP) surfaces from Smithy models where the
  modeled contract maps cleanly to MCP tools, resources, and prompts.
- Defining the runtime and generation boundaries needed so AI-trait-aware
  models remain inspectable, testable, and versionable.

### 10. Honor protocol HTTP-version traits

Protocol traits can declare the HTTP versions a service supports via their `http`
and `eventStreamHttp` members — a list of ALPN protocol IDs in preference order
(for example `@rpcv2Cbor(http: ["h2", "http/1.1"])`). These are currently
ignored: generated clients use the `HttpClient`'s default version (HTTP/1.1
unless configured), with HTTP/2 forced only for native gRPC.

This work includes:

- Reading the `http` / `eventStreamHttp` members at codegen.
- Replacing the runtime's coarse `IProtocol.RequiresHttp2` bool with a
  preferred-version + downgrade-policy model that maps the preference list onto
  ALPN negotiation (request the first supported version, allow downgrade).
- Applying the selected version when the client creates its own `HttpClient` (the
  endpoint constructor and the generated DI helper); documenting that the
  bring-your-own-`HttpClient` and IHttpClientFactory paths configure it
  themselves, since there the caller owns the `HttpClient`.

## Later Work

These are plausible future areas, but they are not the current focus:

- F#-specific generation
