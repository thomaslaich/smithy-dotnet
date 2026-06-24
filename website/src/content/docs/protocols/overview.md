---
title: Overview
description: How Smithy protocols map to generated .NET surfaces in NSmithy.
---

A Smithy protocol trait on a service definition controls two things:

- **Wire format** — how requests and responses are serialized (JSON, XML, CBOR,
  Protobuf)
- **HTTP binding** — how operations, inputs, and outputs map to HTTP methods,
  URIs, headers, and bodies

NSmithy reads the protocol trait and generates the matching client and server
surfaces. Your handler implementation is protocol-agnostic: the same
`IMyServiceHandler` interface is used regardless of which protocol annotates the
service.

## Supported Protocols

| Protocol | Trait | Generated surfaces | Status |
| --- | --- | --- | --- |
| `alloy#simpleRestJson` | `@simpleRestJson` | .NET client, ASP.NET Core server | Preview |
| `aws.protocols#restJson1` | `@restJson1` | .NET client, ASP.NET Core server | Preview |
| `aws.protocols#awsJson1_1` | `@awsJson1_1` | .NET client | Early preview |
| `aws.protocols#awsJson1_0` | `@awsJson1_0` | .NET client | Early preview |
| `aws.protocols#restXml` | `@restXml` | .NET client | Early preview |
| `smithy.protocols#rpcv2Cbor` | `@rpcv2Cbor` | .NET client, ASP.NET Core server | Preview |
| `alloy.proto#grpc` | `@grpc` | gRPC client, ASP.NET Core gRPC server | Experimental |

See [Protocol Status](/smithy-dotnet/protocols/status/) for conformance numbers and maturity details.

## Which Protocol Should I Use?

**[AWS restJson1](/smithy-dotnet/protocols/aws-rest-json1/)** is the
recommended choice for new cross-ecosystem HTTP services. It has broad
compatibility — most official Smithy code generators (Java, TypeScript, Python,
Swift, Rust, Go) target `restJson1` — and NSmithy generates OpenAPI from it,
giving you Scalar UI and standard tooling out of the box.

**[simpleRestJson](/smithy-dotnet/protocols/simple-rest-json/)** is a simpler
alternative if your consumers are exclusively .NET or Scala (via
[Smithy4s](https://disneystreaming.github.io/smithy4s/)). It is the smoothest
current NSmithy end-to-end path, but it has narrower ecosystem reach.

**`alloy.proto#grpc`** is available for teams that need gRPC transport, but
treat it as an early adopter path. It has the least maturity and more explicit
modeling requirements.
