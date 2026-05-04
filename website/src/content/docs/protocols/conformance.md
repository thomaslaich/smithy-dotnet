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

Cases that are not yet supported are marked with explicit skip reasons so the
test output distinguishes between "not implemented" and "broken."

## Running The Tests

```bash
just test
```

Results are printed to the terminal. Each skipped case shows its skip reason
inline.

## Current Coverage

To see the current conformance pass/skip/fail breakdown, run the test suite
locally. The numbers change as coverage expands and should be treated as
point-in-time snapshots rather than a stable published matrix.

Rough guidance on where each protocol stands today:

- `alloy#simpleRestJson` — most complete; the majority of request and response
  cases pass.
- `aws.protocols#restJson1` — narrow slice; only a small subset of cases run,
  with the rest explicitly skipped pending broader binding coverage.
- `smithy.protocols#rpcv2Cbor` — early preview; conformance test integration
  is in progress.
- `alloy.proto#grpc` — not covered by Smithy's conformance suite; tested
  via end-to-end examples instead.

## Skip Reasons

Common reasons a case is skipped rather than failed:

- **Server generation not implemented** — `restJson1` server surfaces and
  malformed-request rejection are out of scope for this preview.
- **Feature not yet implemented** — e.g. open union encodings, greedy label
  URI expansion, endpoint host-prefix binding.
- **AWS service-specific fixtures** — fixtures that depend on AWS-specific
  behavior outside the current generated-client slice.

## Related Docs

- [Protocol Status](../)
- [Known Limitations](../../reference/known-limitations/)
