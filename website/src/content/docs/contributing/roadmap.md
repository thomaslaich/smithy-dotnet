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

- Add support for additional AWS protocol families, especially AWS JSON,
  AWS Query, and EC2 Query.
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

### 5. Implement native proto codecs and first-class gRPC generators

The temporary template-based path is enough to keep examples moving, but it is
not the long-term shape of NSmithy's gRPC support. The next step is to make
proto and gRPC first-class runtime and generator concerns rather than a thin
layer around generated templates.

This work includes:

- Implementing native proto codecs in the runtime and generated code path.
- Generating first-class gRPC clients and servers from Smithy models.
- Tightening the contract between Smithy models, emitted `.proto`, and the
  generated .NET surface so the gRPC path can be tested and versioned as a real
  product surface.

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

## Later Work

These are plausible future areas, but they are not the current focus:

- F#-specific generation
