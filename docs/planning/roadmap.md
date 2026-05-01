# Roadmap

This roadmap describes the current direction of NSmithy as the repository
exists today. It is intentionally shorter and more opinionated than the earlier
phase-by-week plan.

## Architecture

NSmithy keeps a hybrid boundary:

- Smithy CLI handles model assembly, validation, projections, imports, and Maven
  dependencies.
- NSmithy reads the Smithy build output and performs C# and `.proto`
  generation inside the .NET build.

The rationale is documented in [Hybrid Codegen Architecture](../architecture/hybrid-codegen.md).

This means the project is not committing to a rewrite of the generator as a
Smithy Java plugin in the near term, though that remains a reasonable option to
evaluate if the semantic-model boundary becomes a larger cost.

## Current Baseline

The repository already ships a working preview slice:

- Smithy CLI integration through `NSmithy.MSBuild`
- Smithy JSON AST parsing and internal model loading
- C# generation for core shapes
- generated HTTP clients for `aws.protocols#restJson1`
- generated HTTP clients and ASP.NET Core server surfaces for
  `alloy#simpleRestJson`
- `.proto` generation and early gRPC support
- runtime packages for core metadata, JSON, HTTP, client, and server paths
- protocol compliance and end-to-end tests

The roadmap is now about hardening and simplifying this surface, not restarting
the architecture from scratch.

## Principles

1. Keep Smithy as the model front end.
   NSmithy should continue to rely on Smithy CLI for model assembly and
   validation instead of reimplementing Smithy parsing and semantics in .NET.

2. Keep the backend native to .NET.
   The main generator, build integration, and tests should stay in the .NET
   toolchain because the generated artifacts are consumed there.

3. Prefer narrower supported slices over broad but unstable claims.
   Protocol support should grow where the repo already has useful runtime and
   test coverage, especially around `simpleRestJson`.

4. Make preview edges explicit.
   When a feature is partial, document the shape of the boundary rather than
   implying general Smithy support.

5. Remove accidental complexity before adding new protocol families.
   Splitting coupled generation modes and tightening runtime boundaries is more
   valuable than chasing additional protocols too early.

6. Use additional protocol work to validate abstractions.
   Protocol expansion should improve codec and transport boundaries rather than
   just add more generated surface area.

## Near-Term Priorities

### 1. Stabilize the generated HTTP/JSON path

- Expand protocol compliance coverage for generated clients and servers.
- Tighten request/response binding behavior and error handling.
- Close the gap between "works for the example" and "safe preview default."

### 2. Split coupled generation modes

- Decouple client and server generation where they are still emitted together.
- Reduce unnecessary runtime package references for client-only or server-only
  consumers.
- Make generation switches and output shape easier to reason about.

### 3. Improve generator clarity and diagnostics

- Keep generated output predictable and easy to inspect.
- Improve unsupported-shape and unsupported-trait diagnostics.
- Continue simplifying internal generator structure where semantics become hard
  to follow.

### 4. Mature the gRPC path deliberately

- Keep `.proto` generation and gRPC support as an explicit preview track.
- Expand test coverage before broadening feature claims.
- Clarify the model constraints required by the current generated path.

### 5. Bring `rpcv2Cbor` and `restXml` forward

- Start prototyping these earlier than the old roadmap implied.
- Use them to pressure-test codec and transport abstractions beyond the current
  HTTP/JSON path.
- Keep the initial scope narrow and compliance-driven rather than trying to
  claim broad Smithy protocol coverage immediately.

### 6. Revisit serialization generation and AOT support

- Decide whether a Roslyn incremental generator is still the right vehicle for
  serializer metadata or registration glue.
- Improve the story for NativeAOT and reflection trimming only once the main
  generated HTTP/JSON path is stable.

### 7. Tighten packaging and target framework support

- Keep target frameworks aligned with supported .NET releases.
- Avoid expanding package count or package layering without a strong reason.
- Prefer fewer, clearer package boundaries over speculative future package trees.

### 8. Experiment with Java-side Smithy plugin integration

- Evaluate whether selected parts of the codegen pipeline should move into a
  Smithy Java plugin while keeping MSBuild and Smithy CLI as the main user-facing
  build flow.
- Focus the experiment on semantic-model handling and protocol-specific logic
  where direct access to Smithy internals may simplify the implementation.
- Treat this as an architectural spike, not a committed rewrite plan.

## Later Work

These are plausible future areas, but they are not the current focus:

- AWS JSON protocols
- EC2 Query and AWS Query
- F#-specific generation

## Non-Goals For Now

- Rewriting the main generator as a Smithy Java plugin
- Bundling or downloading Smithy CLI from the MSBuild package
- Targeting `restJson1` server generation as a primary roadmap item

## Open Questions

1. How far should `alloy#simpleRestJson` server compliance go before the project
   changes its recommendation level from "preview" to a stronger claim?

2. Should serializer generation return as a distinct package, or should AOT-
   oriented support stay integrated with existing runtime/codegen packages?

3. What is the smallest package and API surface that cleanly supports the gRPC
   path without overfitting to the current preview implementation?

4. When `net8.0` reaches end of support in November 2026, should the project
   drop it immediately and simplify around newer TFMs?

5. Would a Smithy Java plugin, still invoked through the current MSBuild plus
   Smithy CLI flow, actually reduce semantic-model complexity enough to justify
   the additional packaging and testing cost?
