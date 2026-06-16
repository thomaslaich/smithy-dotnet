---
title: Protocol Status
description: Where protocol support stands in the current NSmithy preview.
---

NSmithy is still preview-stage. "Supported" here means there is working
generator and runtime support for a usable slice, not that the protocol is
complete or fully conformant across the Smithy surface.

## Current Status

Conformance is reported separately for the generated **client** and **server**,
each counted against the official cases that actually apply to that direction
(`appliesTo`) in the pinned protocol-test models. A case that only applies to a
server is never counted toward client coverage and vice versa.

| Protocol | Surfaces | Stage | Client | Server |
| --- | --- | --- | --- | --- |
| `alloy#simpleRestJson` | both | Preview (most complete) | 43/43 (100%) | 43/43 (100%) |
| `aws.protocols#restJson1` | both | Preview | 243/247 (98.4%) | 224/227 (98.7%) |
| `aws.protocols#restXml` | client | Early preview | codec-only, no cases yet | — |
| `smithy.protocols#rpcv2Cbor` | both | Preview | 68/68 (100%) | 60/60 (100%) |
| `alloy.proto#grpc` | both | Experimental | tested via examples¹ | tested via examples¹ |

¹ `alloy.proto#grpc` is not covered by Smithy's HTTP conformance suite; it is
validated through end-to-end examples instead, and has the least maturity, the
smallest test surface, and more explicit model requirements such as
`alloy.proto#protoIndex`.

Notes:

- `alloy#simpleRestJson`'s protocol tests all declare `appliesTo: both`; both the
  client and the generated ASP.NET Core server now run every applicable case.
- `aws.protocols#restJson1` exercises both surfaces against nearly every
  applicable case. The handful of unmet cases are curated out of the client
  allowlist; on the server, cases whose operation has no generated handler
  (auxiliary services like Glacier that ship fixtures but aren't part of the
  `RestJson` service) are out of scope rather than counted as failures.
- `aws.protocols#restXml` currently validates the XML codec and transport
  abstractions; no official conformance cases are executed yet.
- `smithy.protocols#rpcv2Cbor`, `alloy#simpleRestJson`, and `aws.protocols#restJson1`
  all exercise both the client and the generated server against their applicable
  cases.

## Recommended Use

- Prefer `alloy#simpleRestJson` if you want the smoothest end-to-end preview path.
- Use `aws.protocols#restJson1` when you need generated AWS-style REST/JSON clients or ASP.NET Core server surfaces.
- Use `smithy.protocols#rpcv2Cbor` for binary CBOR-encoded services — client and server generation are both available.
- Use `aws.protocols#restXml` when you want to evaluate the XML codec path and are comfortable with a smaller preview slice.
- Treat `alloy.proto#grpc` as an early adopter path for teams comfortable working close to generated code and current limitations.

## What "Early Stage" Means Here

In practice, earlier-stage protocols may still have one or more of these traits:

- narrower protocol binding coverage
- fewer end-to-end examples
- less conformance-suite coverage
- more implementation details that are still expected to move
- more explicit project wiring or modeling constraints

## Not Supported Yet

These protocols are not current NSmithy targets:

- AWS JSON protocols
- EC2 Query and AWS Query
