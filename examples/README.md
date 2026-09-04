# NSmithy examples

Runnable examples covering each protocol NSmithy supports. Every example is
self-contained and consumes NSmithy the way an application would: as NuGet
packages, resolved from the locally packed feed in `artifacts/packages`.

All .NET example projects are collected in [`examples.slnx`](examples.slnx).
Broker-backed examples include Docker Compose files for local dependencies.

| Example | Protocol | Shows |
| --- | --- | --- |
| [restjson1/unary](restjson1/unary/) | `aws.protocols#restJson1` | Weather service: REST endpoints, MCP tools, pagination, retries, OpenTelemetry |
| [restjson1/streaming](restjson1/streaming/) | `aws.protocols#restJson1` | Bidirectional restJson1 event streaming (chat service) |
| [simplerestjson](simplerestjson/) | `alloy#simpleRestJson` | Pizza Admin service: unions, enums, maps, errors, API-key auth |
| [rpcv2cbor/unary](rpcv2cbor/unary/) | `smithy.protocols#rpcv2Cbor` | The restJson1 Weather service over CBOR: resources, pagination, errors, retries |
| [rpcv2cbor/streaming](rpcv2cbor/streaming/) | `smithy.protocols#rpcv2Cbor` | Bidirectional rpcv2Cbor event streaming (chat service) |
| [grpc/unary](grpc/unary/) | `alloy.proto#grpc` | Library service over native gRPC (no protoc): proto codec features like sparse maps, oneOf unions, enums |
| [grpc/streaming](grpc/streaming/) | `alloy.proto#grpc` | Bidirectional gRPC event streaming (chat service), with a `Grpc.Net` interop comparison |
| [aws-localstack](aws-localstack/) | AWS JSON, REST XML, REST JSON, AWS Query, EC2 Query | Generated AWS clients with SigV4 signing against LocalStack |
| [polyglot](polyglot/) | `aws.protocols#restJson1` | .NET client calling a Smithy Java server through Docker Compose |
| [kafkajson](kafkajson/) | `bote#kafkaJson` | Device-owned commands/events, generated Kafka consumers, hosting integration, AsyncAPI |
| [redisjson](redisjson/) | `bote#redisStreamsJson` | Durable chat command/event streams and unary inventory request/reply |

## Prerequisites

- .NET 10 SDK
- `just`, or the repository toolchain through `devenv shell`
- Docker for the broker-backed, LocalStack, and polyglot examples

## Build

Run all commands in the example READMEs from the repository root. First build
and pack NSmithy, then restore and build every example against the local
packages:

```bash
just build
just pack
just refresh-examples
```

`just refresh-examples` handles the two build passes required by the gRPC
examples: the first generates `.proto` files and the second compiles them.

Each example's README explains how to run it.
