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

The generator is implemented as a Smithy Java plugin, invoked through the
existing MSBuild and Smithy CLI flow.

## Current Baseline

The repository already ships a working preview slice:

- Smithy CLI integration through `NSmithy.MSBuild`
- C# and `.proto` generation implemented as a Smithy Java plugin
- generated HTTP clients for `aws.protocols#restJson1`
- generated HTTP clients and ASP.NET Core server surfaces for
  `alloy#simpleRestJson`
- early preview protocol slices for `smithy.protocols#rpcv2Cbor` and
  `aws.protocols#restXml`
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
  The main user-facing build flow, package story, and generated-artifact
  consumption should remain natural for .NET users, even if parts of the
  generator eventually move closer to Smithy's Java tooling.

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

### 3. Add templated code generation deliberately

- Introduce templated codegen only where it reduces duplication without hiding
  protocol semantics.
- Use templating to make repeated generated shapes easier to evolve and review.
- Keep the semantic model and generation decisions explicit even if more of the
  emitted source moves through templates.

### 4. Improve generator clarity and diagnostics

- Keep generated output predictable and easy to inspect.
- Improve unsupported-shape and unsupported-trait diagnostics.
- Continue simplifying internal generator structure where semantics become hard
  to follow.

### 5. Mature the gRPC path deliberately

- Keep `.proto` generation and gRPC support as an explicit preview track.
- Expand test coverage before broadening feature claims.
- Clarify the model constraints required by the current generated path.

### 6. Harden the `rpcv2Cbor` and `restXml` slices

- Treat the current implementations as real preview slices, not future
  prototypes.
- Expand compliance and behavioral coverage so these paths reflect stable
  codec and transport seams rather than one-off experiments.
- Keep the scope narrow and compliance-driven instead of claiming broad Smithy
  protocol coverage too early.

## Later Work

These are plausible future areas, but they are not the current focus:

- AWS JSON protocols
- EC2 Query and AWS Query
- F#-specific generation
