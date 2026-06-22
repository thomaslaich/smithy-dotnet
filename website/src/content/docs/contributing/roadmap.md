---
title: Roadmap
description: Current direction and near-term priorities for NSmithy.
---

This roadmap describes the current direction of NSmithy as it exists today.
The architecture is no longer the open question: NSmithy uses Smithy CLI for
model assembly and a Smithy Java plugin for generation, integrated into the
.NET build through `NSmithy.MSBuild`. The roadmap is about hardening and
expanding that baseline rather than revisiting it.

## Direction

- Keep Smithy CLI as the model front end for assembly, validation, projections,
  and Maven dependency resolution.
- Keep the generated output and runtime story natural for .NET consumers.
- Prefer explicit preview boundaries over broad compatibility claims.
- Use protocol expansion to validate and strengthen the runtime seams that are
  already in place.

## Near-Term Priorities

### 1. Expand AWS protocol coverage and AWS readiness

- Expand AWS protocol coverage beyond the initial AWS JSON client support,
  especially AWS Query and EC2 Query.
- Continue hardening `aws.protocols#restJson1`, `aws.protocols#restXml`, and
  `smithy.protocols#rpcv2Cbor` as real preview surfaces.
- Implement AWS authentication support, especially SigV4 signing driven by
  `@aws.auth#sigv4`, so generated clients can call real AWS-compatible
  endpoints.
- Add an integration test suite against LocalStack to validate generated AWS
  clients against realistic protocol, signing, and endpoint behavior.
- Keep the scope driven by conformance and real runtime behavior rather than by
  marketing-level protocol checklists.

### 2. XML doc comments from Smithy documentation traits

Smithy's `@documentation` trait and `///` doc comments are not yet emitted as
C# XML doc comments (`/// <summary>…</summary>`) on generated types and members.
Adding this would improve the IDE experience for consumers of generated code —
hover documentation, parameter hints, and IntelliSense would reflect the
model's documentation rather than being empty.

### 3. Improve generator clarity and diagnostics

- Keep generated output predictable and easy to inspect.
- Improve unsupported-shape and unsupported-trait diagnostics.
- Continue simplifying generator internals where semantics are harder to follow
  than they need to be.

### 4. Improve CBOR and XML codec performance through schema-compiled codecs

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

### 5. Add streaming operations, including bidirectional streaming

NSmithy's current generated clients and servers are unary request/response
surfaces. The next larger protocol shape is streaming: client streaming, server
streaming, and bidirectional streaming. This is especially important for gRPC,
but the runtime and generator model should not bake in gRPC-only assumptions.

This work includes:

- Modeling streaming operation inputs and outputs in the generated C# surface.
- Defining transport/runtime abstractions for client, server, and bidirectional
  streams.
- Supporting gRPC streaming over HTTP/2 while keeping the design usable for
  future streaming protocols.
- Adding end-to-end tests that cover backpressure, cancellation, errors, and
  stream completion behavior.

### 6. Expand to async protocols

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

### 7. Support Smithy AI traits and MCP generation

Smithy's AI-oriented traits open up another important integration surface for
NSmithy. Supporting them cleanly should make it possible to generate useful
.NET and protocol artifacts for tool-driven and agent-driven workflows rather
than treating them as out-of-band metadata.

This work includes:

- Supporting relevant Smithy AI traits during model interpretation and codegen.
- Generating Model Context Protocol (MCP) surfaces from Smithy models where the
  modeled contract maps cleanly to MCP tools, resources, and prompts.
- Defining the runtime and generation boundaries needed so AI-trait-aware
  models remain inspectable, testable, and versionable.

### 8. Honor protocol HTTP-version traits

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
