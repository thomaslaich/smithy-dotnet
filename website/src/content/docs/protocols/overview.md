---
title: Protocols
description: Choose a Smithy protocol and understand what it changes in generated .NET clients and servers.
---

NSmithy binds modeled operations to the protocols below. For the separation
between a contract and its wire representation, see
[Protocol bindings](/smithy-dotnet/concepts/modeling/#add-protocol-bindings).

## Choose a protocol

| Protocol | Generated APIs | Choose it for |
| --- | --- | --- |
| [`aws.protocols#restJson1`](../rest-json/) | Client and server | General REST APIs, broad tooling support, streaming, and AWS-compatible behavior |
| [`smithy.protocols#rpcv2Cbor`](../rpc-v2-cbor/) | Client and server | Compact binary RPC with CBOR and event streaming |
| [`aws.protocols#awsJson1_1`](../aws-json/) | Client | Existing AWS JSON RPC services |
| [`aws.protocols#awsJson1_0`](../aws-json/) | Client | Existing AWS JSON 1.0 services |
| [`aws.protocols#awsQuery`](../aws-query/) | Client | Existing AWS Query services |
| [`aws.protocols#ec2Query`](../aws-ec2-query/) | Client | Existing EC2 Query services |
| [`aws.protocols#restXml`](../rest-xml/) | Client | Existing AWS XML services such as S3 |
| [`alloy#simpleRestJson`](../rest-json/) | Client and server | Alloy and Smithy4s interoperability |
| [`alloy.proto#grpc`](../grpc/) | Client and server | Standard gRPC and protobuf interoperability |

For most new HTTP APIs, start with `restJson1`. Use `rpcv2Cbor` for compact
binary Smithy RPC between compatible peers. Use gRPC when standard protobuf and
gRPC interoperability matter. The AWS Query, AWS JSON, and restXml protocols
are primarily for existing AWS services and emulators.

See [Protocol Status](../status/) for maturity and current conformance numbers.

## Services with multiple protocols

A service can declare more than one supported protocol. Generated clients can
select a non-default protocol through their configuration, and generated servers
can map several protocols to the same handler.

See [Hosting and Multiple Protocols](/smithy-dotnet/servers/hosting/) for route
mapping and [Client Configuration](/smithy-dotnet/guides/client-configuration/)
for protocol selection.
