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

Current snapshot:

- `alloy#simpleRestJson` — `43/43` official cases (`100%`).
- `aws.protocols#restJson1` — `234/272` official cases (`86.03%`).
- `smithy.protocols#rpcv2Cbor` — early preview; conformance test integration
  is in progress.
- `alloy.proto#grpc` — not covered by Smithy's conformance suite; tested
  via end-to-end examples instead.

Two different numbers are useful when reading the test projects:

- executable allowlist coverage: the cases the local conformance project is
  expected to run and pass
- full official corpus coverage: the total number of official request/response
  cases present in the pinned models

For `simpleRestJson`, those numbers now line up because the full official corpus
is executable. For `restJson1`, they do not: some remaining official cases are
still outside the current client-executable surface or are more meaningful with
server-side behavior.

## Skip Reasons

Common reasons a case is skipped rather than failed:

- **Server generation not implemented** — `restJson1` server surfaces and
  malformed-request rejection are out of scope for this preview.
- **Feature not yet implemented** — e.g. broader `restJson1` binding coverage,
  additional request/response edge cases, or future malformed-input handling.
- **AWS service-specific fixtures** — fixtures that depend on AWS-specific
  behavior outside the current generated-client slice.

## Related Docs

- [Protocol Status](../)
- [Known Limitations](../../reference/known-limitations/)
