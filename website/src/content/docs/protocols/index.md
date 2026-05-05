---
title: Protocol Status
description: Where protocol support stands in the current NSmithy preview.
---

This page is the short version of where protocol support stands today.

NSmithy is still preview-stage. "Supported" here means there is working
generator and runtime support for a usable slice, not that the protocol is
complete or fully conformant across the Smithy surface.

## Current Status

| Protocol | Generated Surfaces | Stage | Notes |
| --- | --- | --- | --- |
| `alloy#simpleRestJson` | .NET client, ASP.NET Core server | Preview, most complete | Best-covered transport today. Main path for generated HTTP clients and servers. |
| `aws.protocols#restJson1` | .NET client | Early preview | Client generation works, but coverage is narrower and the project is not targeting AWS-style server generation. |
| `aws.protocols#restXml` | .NET client | Early preview | Client generation available; used to validate the XML codec and transport abstractions. |
| `smithy.protocols#rpcv2Cbor` | .NET client | Early preview | Client-only slice that exercises the binary document codec seam directly. |
| `alloy.proto#grpc` | `.proto` emission, gRPC client adapter, ASP.NET Core gRPC server adapter | Experimental | Works for the current generated path, but still has the least maturity, the smallest test surface, and more explicit model requirements such as `alloy.proto#protoIndex`. |

## Recommended Use

- Prefer `alloy#simpleRestJson` if you want the smoothest end-to-end preview path.
- Use `aws.protocols#restJson1` when you need generated AWS-style REST/JSON clients.
- Use `aws.protocols#restXml` or `smithy.protocols#rpcv2Cbor` when you want to evaluate the XML or binary codec paths and are comfortable with a smaller preview slice.
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

## Related Docs

- [Known Limitations](../reference/known-limitations/)
- [Multi-Protocol Guide](../guides/multi-protocol/)
- [Architecture](../design/codegen-architecture/)
- [Conformance Matrix](./conformance/)
- [Roadmap](../contributing/roadmap/)
