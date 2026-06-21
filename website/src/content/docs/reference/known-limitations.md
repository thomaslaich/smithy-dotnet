---
title: Known Limitations
description: Current limitations and rough edges in the NSmithy preview.
---

NSmithy is a preview-stage implementation. This page lists the larger gaps and
rough edges so you can judge whether a given model is a good fit today.

## Protocol Coverage Is Still Narrow

Support is intentionally selective, and protocols are at different maturity
levels. Each implemented protocol has a conformance suite run against the
official Smithy / AWS protocol tests (`tests/Conformance`):

- **`alloy#simpleRestJson`** — the most complete path: client and ASP.NET Core
  server, the best end-to-end coverage.
- **`aws.protocols#restJson1`** — client and ASP.NET Core server. The main
  remaining corpus gap is the Glacier-specific fixture set, which needs broader
  projection support.
- **`smithy.protocols#rpcv2Cbor`** — client and ASP.NET Core server, with
  conformance coverage.
- **`aws.protocols#restXml`** — client only by design (see the server note
  below); narrower coverage than the JSON paths.
- **`alloy.proto#grpc`** — native client and server (see below); the least
  mature path.

Not yet implemented (planned as **clients** — servers are not, see below):

- AWS JSON protocols
- EC2 Query and AWS Query

## Streaming Is Not Supported

NSmithy generates **unary request/response** operations only. The `@streaming`
trait is not yet honored, for **any** protocol:

- No client streaming, server streaming, or bidirectional streaming.
- No event streams (`@streaming` on a union).
- No streaming payloads (`@streaming` on a blob); blob payloads are buffered as
  `byte[]` rather than streamed.

This applies to every protocol, including gRPC — where streaming RPCs are most
commonly expected. Models that declare streaming members will not get a correct
streaming surface. Adding streaming (including bidirectional) is a tracked
near-term priority; see the [Roadmap](/smithy-dotnet/contributing/roadmap/).

## gRPC Is Experimental

gRPC is a **native** path — its own protobuf codec (`NSmithy.Codecs.Proto`) and
gRPC transport binding (`NSmithy.Protocols.Grpc`) over HTTP/2, with no `protoc`,
`Grpc.Tools`, or `Grpc.Net` dependency. It should still be treated as an
early-adopter track:

- smaller test and example coverage than the HTTP/JSON paths
- stricter model requirements, such as `alloy.proto#protoIndex` on members
- unary only — no streaming RPCs yet (see above)
- implementation details that are still expected to move

## Servers: ASP.NET Core Only, Service-Oriented Protocols Only

Server generation targets ASP.NET Core, and only for the protocols you would
implement a service in: `alloy#simpleRestJson`, `aws.protocols#restJson1`,
`smithy.protocols#rpcv2Cbor`, and native gRPC.

The AWS-facing protocols — `aws.protocols#restXml`, AWS JSON, and AWS / EC2
Query — are **client-only by design**. NSmithy generates clients to call
AWS-compatible services; servers for those protocols are not planned.

Other constraints:

- No general non-ASP.NET server story.
- Response binding and error behavior still need broader conformance coverage.

## HTTP Version Negotiation Traits Are Ignored

The `http` / `eventStreamHttp` members on protocol traits (for example
`@rpcv2Cbor(http: ["h2"])`) are not honored. Generated clients use the
`HttpClient`'s default HTTP version (HTTP/1.1 unless configured), and HTTP/2 is
forced only for native gRPC. See the [Roadmap](/smithy-dotnet/contributing/roadmap/).

## Validation Is Incomplete

Generated constructors do not implement full Smithy validation semantics.
Generated C# nullability is authoritative, but external request binding and
deserialization still need more protocol-aware runtime validation.

## Codec Performance And AOT Are Still Maturing

The codecs (`NSmithy.Codecs.Json`, `Cbor`, `Xml`, `Proto`) are schema-driven —
they use the codegen-emitted typed accessors on the schema, with **no runtime
reflection**. JSON goes a step further and compiles a per-shape reader/writer
once from the schema, caching structural decisions such as dispatch and boxing.
CBOR, XML, and Proto still walk the schema on each call; compiling them once (as
JSON does) is a tracked performance item on the
[Roadmap](/smithy-dotnet/contributing/roadmap/).

The codecs are also not yet validated or optimized for:

- NativeAOT
- source-generated serializer metadata
- every Smithy edge case across future protocol families

## Generated Model Scope Can Be Too Broad By Default

By default, the generator emits all supported shapes in the assembled model.
When using Smithy build dependencies for traits or shared model packages,
configure `SmithyBaseNamespace` so dependency model shapes are not emitted as C#.
