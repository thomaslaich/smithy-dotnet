---
title: Roadmap
description: Current direction and near-term priorities for NSmithy.
---

Current coverage is documented in [Protocol status](/smithy-dotnet/protocols/status/)
and the [changelog](https://github.com/thomaslaich/smithy-dotnet/blob/main/CHANGELOG.md).
The following areas remain under development or consideration.

## Near-term priorities

### 1. Expand AWS protocol coverage and AWS readiness

- Keep the fully conformant AWS Query and EC2 Query clients green while
  expanding real-service coverage.
- Keep `aws.protocols#restJson1` and `smithy.protocols#rpcv2Cbor` fully
  conformant, and continue hardening
  `aws.protocols#restXml` as a preview implementation.
- Build on modeled endpoint rules, profile/SSO/IMDS credentials, presigning,
  and published AWS signing test vectors with additional credential sources
  and SigV4a.
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
  client/server interfaces beyond HTTP-centric assumptions.
