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

### 1. Expand AWS protocol coverage

- Add support for additional AWS protocol families, especially AWS JSON,
  AWS Query, and EC2 Query.
- Continue hardening `aws.protocols#restJson1`, `aws.protocols#restXml`, and
  `smithy.protocols#rpcv2Cbor` as real preview surfaces.
- Keep the scope driven by conformance and real runtime behavior rather than by
  marketing-level protocol checklists.

### 2. XML doc comments from Smithy documentation traits

Smithy's `@documentation` trait and `///` doc comments are not yet emitted as
C# XML doc comments (`/// <summary>…</summary>`) on generated types and members.
Adding this would improve the IDE experience for consumers of generated code —
hover documentation, parameter hints, and IntelliSense would reflect the
model's documentation rather than being empty.

### 3. OpenAPI spec generation

Wire `smithy-openapi` into the MSBuild pipeline so that `dotnet build` emits an
`openapi.json` alongside the generated C# files. Because the spec is derived
from the Smithy model it stays in sync with the source of truth automatically.
The generated spec can then be served at runtime via Swagger UI or .NET 9's
`Microsoft.AspNetCore.OpenApi` middleware without the overhead of runtime
reflection over ASP.NET Core endpoints.

### 4. Improve generator clarity and diagnostics

- Keep generated output predictable and easy to inspect.
- Improve unsupported-shape and unsupported-trait diagnostics.
- Continue simplifying generator internals where semantics are harder to follow
  than they need to be.

### 5. Expand the `dotnet nsmithy` tool

- Grow `dotnet nsmithy` into the .NET workflow companion for NSmithy rather
  than a second general-purpose Smithy CLI.
- Add project-bootstrap ergonomics such as `init` or scaffold commands for
  common NSmithy layouts and examples.
- Keep the tool focused on .NET-specific workflows such as scaffolding,
  packaging, publishing, and diagnostics while leaving Smithy model semantics
  to Smithy CLI.

### 6. Mature the gRPC path

- Keep `.proto` generation and gRPC support as an explicit preview track.
- Expand test coverage before broadening feature claims.
- Clarify the model constraints required by the current generated path.

### 7. Split the C# and `.proto` code generators

`ProtoGenerator` currently lives inside `smithy-csharp-codegen` alongside the
C# generators. As the gRPC path matures it should move into its own module
(`smithy-proto-codegen` or similar) so that:

- The C# generator has no dependency on proto concerns.
- The `.proto` generator can evolve, be versioned, and be distributed
  independently.
- Consumers who only need C# output do not pull in proto generation logic.

### 8. AWS authentication — SigV4 signing

Real AWS services require [SigV4](https://docs.aws.amazon.com/general/latest/gr/signature-version-4.html)
request signing via the `@aws.auth#sigv4` trait. Currently, generated clients
send unsigned requests and cannot authenticate against AWS endpoints.

This work involves:

- Recognising `@aws.auth#sigv4` (and related auth traits) during codegen.
- Providing a signing middleware or hook in `NSmithy.Client` so credentials can
  be injected into the request pipeline.
- Defining a credential-provider abstraction that fits the existing
  `SmithyClientOptions` / `ISmithyClientMiddleware` model.

Until this is in place, `aws.protocols#restJson1` clients work correctly for
protocol-level correctness (conformance tests pass) but cannot call real AWS
services.

## Later Work

These are plausible future areas, but they are not the current focus:

- F#-specific generation
