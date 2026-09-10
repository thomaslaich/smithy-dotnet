---
title: RPC v2 CBOR
description: Binary CBOR RPC over HTTP using smithy.protocols#rpcv2Cbor, with unary and streaming client and server support.
---

`smithy.protocols#rpcv2Cbor` is a compact binary RPC protocol defined by Smithy.
NSmithy generates typed clients and ASP.NET Core servers for unary and event
stream operations.

Use it when you control both peers and want smaller binary messages without
adding protobuf field numbers to the model. Use [REST JSON](../rest-json/) when
HTTP resources, routes, and broad REST tooling are more important.

See [Protocol Status](../status/) for maturity and current conformance numbers.

## Protocol behavior

| Area | rpcv2Cbor |
| --- | --- |
| Route | `POST /service/{Service}/operation/{Operation}` |
| Body | Binary CBOR |
| Content type | `application/cbor` |
| Operation header | `Smithy-Protocol: rpc-v2-cbor` |
| Errors | Modeled type in the CBOR `__type` member |
| Streaming | Input, output, and duplex event streams |
| HTTP bindings | Not used |

## Modeling

Apply `@rpcv2Cbor` to the service. The protocol derives routes automatically,
so operations do not use `@http`:

```smithy
$version: "2"

namespace example.weather

use smithy.protocols#rpcv2Cbor

@rpcv2Cbor
service Weather {
    version: "2026-01-01"
    operations: [GetCity]
}

operation GetCity {
    input := {
        @required
        cityId: String
    }
    output := {
        @required
        name: String
    }
    errors: [NoSuchResource]
}

@error("client")
structure NoSuchResource {
    @required
    resourceType: String
}
```

No additional Maven dependency is needed. The `smithy.protocols` shapes are
bundled with the Smithy CLI.

## On the wire

Every operation is a POST to a path derived from the service and operation
names:

```http
POST /service/Weather/operation/GetCity HTTP/1.1
Host: api.example.com
Smithy-Protocol: rpc-v2-cbor
Content-Type: application/cbor
Accept: application/cbor

<CBOR {"cityId": "123"}>

HTTP/1.1 200 OK
Smithy-Protocol: rpc-v2-cbor
Content-Type: application/cbor

<CBOR {"name": "Seattle"}>
```

The notation above describes the decoded values. The actual body contains CBOR
bytes.

## Streaming

A streaming operation places an `@streaming` union in its input, output, or
both. Generated shapes expose the stream as `IAsyncEnumerable<TEvent>`.

rpcv2Cbor carries event messages in a framed stream and encodes modeled event
payloads as CBOR. NSmithy supports server streaming, client streaming, and
bidirectional streaming. Initial non-streaming members stay on the modeled input
or output around the event stream.

## Packages

| Surface | Packages |
| --- | --- |
| Client | `NSmithy.Client`, `NSmithy.Codecs.Cbor`, `NSmithy.Protocols.RpcV2Cbor` |
| Server | `NSmithy.Server.AspNetCore`, `NSmithy.Codecs.Cbor`, `NSmithy.Protocols.RpcV2Cbor` |

## Examples

- [Unary rpcv2Cbor weather service](https://github.com/thomaslaich/smithy-dotnet/tree/main/examples/rpcv2cbor/unary)
- [Streaming rpcv2Cbor chat service](https://github.com/thomaslaich/smithy-dotnet/tree/main/examples/rpcv2cbor/streaming)

## Specification and tests

- [Smithy RPC v2 CBOR specification](https://smithy.io/2.0/additional-specs/protocols/smithy-rpc-v2.html)
- [Official rpcv2Cbor protocol tests](https://github.com/smithy-lang/smithy/tree/main/smithy-protocol-tests/model/rpcv2Cbor)
