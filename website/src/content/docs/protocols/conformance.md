---
title: Protocol Conformance
description: How NSmithy runs protocol conformance tests.
---

NSmithy runs official Smithy and Alloy protocol conformance test suites as part
of its test infrastructure. The tests are located under `packages/` in the
repository and are run as part of the standard `just test` CI step.

## How Conformance Tests Work

Each supported protocol has a dedicated test project that:

1. Loads the official Smithy conformance test suite for that protocol.
2. Generates request or response messages using NSmithy's codec and protocol
   binding.
3. Compares the result against the expected wire representation from the test
   case.

Cases that are not yet supported are kept out of the executable allowlists, so
the test output distinguishes between the runnable surface and the broader
official corpus.

## Running The Tests

```bash
just test
```

Results are printed to the terminal.

## Current Coverage

The numbers below are point-in-time snapshots from the pinned protocol-test
models in this repository. They count official Smithy/Alloy
`httpRequestTests` plus `httpResponseTests`.

Coverage is counted per direction, against the official cases that apply to that
direction (`appliesTo`):

| Protocol | Client | Server |
| --- | --- | --- |
| `alloy#simpleRestJson` | `43/43` (`100%`) | not yet exercised |
| `aws.protocols#restJson1` | `243/247` (`98.4%`) | `19/227` (`8.4%`) |
| `smithy.protocols#rpcv2Cbor` | `68/68` (`100%`) | `60/60` (`100%`) |
| `alloy.proto#grpc` | not covered by Smithy's suite (tested via examples) | — |

A few things worth knowing when reading the numbers:

- The denominator for each cell is the number of official cases that apply to
  that direction, so a server-only case is never counted toward client coverage.
- `alloy#simpleRestJson` tests are all `appliesTo: both`; there is no server
  conformance project driving them yet, so server is "not yet exercised" rather
  than `0%`.
- `restJson1`'s remaining client gap is the Glacier-specific fixture slice; its
  server surface is still early.

## Skip Reasons

Common reasons a case is skipped rather than failed:

- **Fixture projection not yet wired in** — e.g. the remaining Glacier-specific
  `restJson1` cases.
- **Feature not yet implemented** — e.g. broader malformed-input handling or
  future protocol/runtime edge cases outside the current pinned corpus.
- **AWS service-specific fixtures** — fixtures that depend on AWS-specific
  behavior outside the current generated-client slice.
