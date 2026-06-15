---
title: Overview
description: How Smithy protocols map to generated .NET surfaces in NSmithy.
---

A Smithy protocol trait on a service definition controls two things:

- **Wire format** — how requests and responses are serialized (JSON, XML, CBOR, Protobuf)
- **HTTP binding** — how operations, inputs, and outputs map to HTTP methods, URIs, headers, and bodies

NSmithy reads the protocol trait and generates the matching client and server surfaces. Your handler implementation is protocol-agnostic — the same `IMyServiceHandler` interface is used regardless of which protocol the service is annotated with.

## Supported Protocols

| Protocol | Trait | Generated surfaces | Status |
| --- | --- | --- | --- |
| `alloy#simpleRestJson` | `@simpleRestJson` | .NET client, ASP.NET Core server | Preview |
| `aws.protocols#restJson1` | `@restJson1` | .NET client, ASP.NET Core server | Preview |
| `aws.protocols#restXml` | `@restXml` | .NET client | Early preview |
| `smithy.protocols#rpcv2Cbor` | `@rpcv2Cbor` | .NET client, ASP.NET Core server | Preview |
| `alloy.proto#grpc` | `@grpc` | gRPC client, ASP.NET Core gRPC server | Experimental |

See [Protocol Status](/smithy-dotnet/protocols/status/) for conformance numbers and maturity details.

## Which Protocol Should I Use?

**`aws.protocols#restJson1`** is the recommended choice for new services. It has broad cross-ecosystem compatibility — most official Smithy code generators (Java, TypeScript, Python, Swift, Rust, Go) target `restJson1` — and NSmithy generates OpenAPI from it, giving you Scalar UI and standard tooling out of the box.

**`alloy#simpleRestJson`** is a simpler alternative if your consumers are exclusively .NET or Scala (via [Smithy4s](https://disneystreaming.github.io/smithy4s/)). It has 100% conformance coverage but is narrower in ecosystem reach.

**`alloy.proto#grpc`** is available for teams that need gRPC transport, but treat it as an early adopter path — it has the least maturity and more explicit modeling requirements.
