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

- Move AWS protocol work into the main near-term track.
- Add support for additional AWS protocol families, especially AWS JSON,
  AWS Query, and EC2 Query.
- Keep the scope driven by conformance and real runtime behavior rather than by
  marketing-level protocol checklists.

### 2. Deepen the current AWS protocol slices

- Continue hardening `aws.protocols#restJson1`, `aws.protocols#restXml`, and
  `smithy.protocols#rpcv2Cbor` as real preview surfaces.
- Expand protocol compliance and end-to-end coverage where the current runtime
  seams already exist.
- Tighten request/response binding behavior and protocol-specific error
  handling.

### 3. Keep the REST JSON path strong

- Maintain `alloy#simpleRestJson` as the most complete end-to-end path.
- Keep client and ASP.NET Core server generation stable as new protocol work is
  added.
- Continue using the REST/JSON path as the main preview baseline for generated
  developer experience.

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

## Later Work

These are plausible future areas, but they are not the current focus:

- F#-specific generation
