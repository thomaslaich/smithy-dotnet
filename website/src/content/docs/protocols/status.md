---
title: Protocol Status
description: Where protocol support stands in the current NSmithy preview.
---

NSmithy is still preview-stage. "Supported" here means there is working
generator and runtime support for a usable slice, not that the protocol is
complete or fully conformant across the Smithy surface.

## Current Status

| Protocol | Generated Surfaces | Stage | Notes |
| --- | --- | --- | --- |
| `alloy#simpleRestJson` | .NET client, ASP.NET Core server | Preview, most complete | Best-covered transport today. Official pinned-suite coverage is currently `43/43` (`100%`). |
| `aws.protocols#restJson1` | .NET client, ASP.NET Core server | Preview | Client and server generation work. Official pinned-suite coverage is currently `268/272` (`98.53%`), with the remaining gap concentrated in Glacier-specific fixtures. |
| `aws.protocols#restXml` | .NET client | Early preview | Client generation available; used to validate the XML codec and transport abstractions. |
| `smithy.protocols#rpcv2Cbor` | .NET client, ASP.NET Core server | Preview | Client and server generation. Official pinned-suite client coverage: `47/84` (`56%`). |
| `alloy.proto#grpc` | `.proto` emission, gRPC client adapter, ASP.NET Core gRPC server adapter | Experimental | Works for the current generated path, but still has the least maturity, the smallest test surface, and more explicit model requirements such as `alloy.proto#protoIndex`. |

## Current Conformance Snapshot

All numbers are client-side request/response test cases from the official Smithy
protocol test models pinned in this repository. They are broader than the
executable allowlists used by individual test projects, but narrower than every
test that exists upstream.

| Protocol | Cases | Pass | Rate |
| --- | --- | --- | --- |
| `alloy#simpleRestJson` | 43 | 43 | 100% |
| `aws.protocols#restJson1` | 272 | 268 | 98.5% |
| `smithy.protocols#rpcv2Cbor` | 84 | 47 | 56% |
| combined | 399 | 358 | 89.7% |

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
