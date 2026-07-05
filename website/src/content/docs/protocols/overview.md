---
title: Protocols
description: Choose a Smithy protocol and understand what it changes in generated .NET clients and servers.
---

A protocol defines the transport bindings, body encoding, error format, and
streaming behavior for a Smithy service. For HTTP protocols that includes routes,
methods, and headers; for messaging protocols it includes topics, keys, and
message headers. NSmithy reads the protocol trait and generates the matching .NET
runtime bindings.

The protocol does not change your application model. Generated clients expose
typed operations, and generated servers expose typed handler interfaces,
regardless of the wire format.

## Choose a protocol

| Protocol | Generated surfaces | Choose it for |
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
| [`bote#kafkaJson`](../bote-kafka-json/) | Kafka producer and consumers | Typed asynchronous messaging over Kafka |

For most new HTTP APIs, start with `restJson1`. Use `rpcv2Cbor` for compact
binary Smithy RPC between compatible peers. Use gRPC when standard protobuf and
gRPC interoperability matter. The AWS Query, AWS JSON, and restXml protocols
are primarily for existing AWS services and emulators. Use `kafkaJson` only when
you specifically need Kafka messaging; its generated surface is a producer and
consumers rather than the HTTP client/server pair.

See [Protocol Status](../status/) for maturity and current conformance numbers.

## What changes with the protocol

- Request routes, methods, and required headers
- JSON, XML, CBOR, or protobuf body encoding
- Error discriminators and response envelopes
- Streaming framing and HTTP version requirements
- The protocol runtime and codec packages used by generated code

REST protocols also use Smithy HTTP binding traits such as `@http`,
`@httpLabel`, `@httpQuery`, and `@httpHeader`. RPC protocols derive their routes
from the service and operation names.

## What stays the same

The generated client keeps the same typed operation surface:

```csharp
using Example.Weather;

var client = new WeatherClient(new Uri("https://api.example.com"));
var city = await client.GetCityAsync(new GetCityInput("SEA"));
Console.WriteLine(city.Name);
```

Generated servers use a handler interface with one method per operation. The
adapter handles routing, serialization, validation, and modeled errors before
or after the handler call.

Changing a service protocol does not require changes to handler code or client
call sites if the model stays within the feature set shared by both protocols.

## Services with multiple protocols

A service can declare more than one supported protocol. Generated clients can
select a non-default protocol through their configuration, and generated servers
can map several protocol surfaces to the same handler.

See [Hosting and Multiple Protocols](/smithy-dotnet/servers/hosting/) for route
mapping and [Client Configuration](/smithy-dotnet/guides/client-configuration/)
for protocol selection.
