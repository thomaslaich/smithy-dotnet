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

- Keep the fully conformant AWS Query and EC2 Query client surfaces green while
  expanding real-service coverage.
- Continue hardening `aws.protocols#restJson1`, `aws.protocols#restXml`, and
  `smithy.protocols#rpcv2Cbor` as preview surfaces.
- Build on regional endpoint resolution, profile/SSO/IMDS credentials,
  presigning, and published AWS golden vectors with modeled endpoint rule sets,
  additional credential sources, and SigV4a.
- Grow the LocalStack integration coverage beyond the initial example into a
  broader suite that validates generated AWS clients against realistic protocol,
  signing, and endpoint behavior.
- Keep the scope driven by conformance and observed runtime behavior rather
  than by protocol checklists.

### 2. Expand to async protocols

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

### 3. Support Smithy AI traits and MCP generation

Support Smithy's AI-oriented traits so that .NET and protocol artifacts can be
generated for tool-driven and agent-driven workflows, rather than treating the
traits as out-of-band metadata.

This work includes:

- Supporting relevant Smithy AI traits during model interpretation and codegen.
- Generating Model Context Protocol (MCP) surfaces from Smithy models where the
  modeled contract maps cleanly to MCP tools, resources, and prompts.
- Defining the runtime and generation boundaries needed so AI-trait-aware
  models remain inspectable, testable, and versionable.

## Later Work

These are plausible future areas, but they are not the current focus:

- F#-specific generation
