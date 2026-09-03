# Examples

Runnable examples covering each protocol NSmithy supports. Every example is
self-contained and consumes NSmithy the way an application would: as NuGet
packages, resolved from the locally packed feed in `artifacts/packages`.

All .NET example projects are collected in [`examples.slnx`](examples.slnx).
Broker-backed examples also include Docker Compose files for local infrastructure.

| Example | Protocol | Shows |
| --- | --- | --- |
| [restjson1/unary](restjson1/unary/) | `aws.protocols#restJson1` | Weather service: REST endpoints, MCP tools, pagination, retries, OpenTelemetry |
| [restjson1/streaming](restjson1/streaming/) | `aws.protocols#restJson1` | Bidirectional restJson1 event streaming (chat service) |
| [simplerestjson](simplerestjson/) | `alloy#simpleRestJson` | Pizza Admin service: unions, enums, maps, errors, API-key auth |
| [rpcv2cbor/unary](rpcv2cbor/unary/) | `smithy.protocols#rpcv2Cbor` | The restJson1 Weather service over CBOR: resources, pagination, errors, retries |
| [rpcv2cbor/streaming](rpcv2cbor/streaming/) | `smithy.protocols#rpcv2Cbor` | Bidirectional rpcv2Cbor event streaming (chat service) |
| [grpc/unary](grpc/unary/) | `alloy.proto#grpc` | Library service over native gRPC (no protoc): proto codec features like sparse maps, oneOf unions, enums |
| [grpc/streaming](grpc/streaming/) | `alloy.proto#grpc` | Bidirectional gRPC event streaming (chat service), with a `Grpc.Net` interop comparison |
| [aws-localstack](aws-localstack/) | AWS JSON, restXml, restJson1 | Generated AWS clients with SigV4 signing against LocalStack |
| [polyglot](polyglot/) | `aws.protocols#restJson1` | .NET client calling a Smithy Java server, via docker-compose |
| [Kafka JSON](kafkajson/) | `bote#kafkaJson` | Device-owned commands/events, generated Kafka consumers, hosting integration, AsyncAPI |
| [Redis JSON](redisjson/) | `bote#redisStreamsJson` | Durable chat command/event streams and unary inventory request/reply |

## Building

Pack the NSmithy packages first, then build the solution:

```shell
just build pack
dotnet build examples/examples.slnx
```

The gRPC examples need two build passes on a clean tree: the first pass
generates the `.proto` files, the second compiles them. If the first build
fails in the gRPC projects, build again.

Each example's README explains how to run it.
