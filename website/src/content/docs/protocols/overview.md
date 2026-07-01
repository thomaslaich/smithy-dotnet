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
service. See [Client & Server Usage](/smithy-dotnet/protocols/usage/) for the
canonical handler and client code — the protocol pages below only cover what is
specific to each protocol (its trait, modeling rules, and wire format).

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

See [Protocol Status](/smithy-dotnet/protocols/status/) for conformance numbers,
maturity details, and guidance on which protocol to choose.
