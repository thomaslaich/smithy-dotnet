---
title: RPC v2 JSON
description: JSON RPC over HTTP using smithy.protocols#rpcv2Json, with unary and streaming client and server support.
---

`smithy.protocols#rpcv2Json` is the JSON variant of Smithy's RPC v2 protocol.
It shares routing, headers, errors, and event streams with
[RPC v2 CBOR](../rpc-v2-cbor/) and carries JSON bodies instead of CBOR.
NSmithy generates typed clients and ASP.NET Core servers for unary and event
stream operations.

See [Protocol Status](../status/) for maturity and current conformance numbers.

## Protocol behavior

| Area | rpcv2Json |
| --- | --- |
| Route | `POST /service/{Service}/operation/{Operation}` |
| Body | JSON |
| Content type | `application/json` |
| Operation header | `Smithy-Protocol: rpc-v2-json` |
| Errors | Modeled type in the JSON `__type` member |
| Streaming | Input, output, and duplex event streams |
| HTTP bindings | Not used |

JSON serialization differs from `restJson1` in three places:

| Shape or trait | rpcv2Json |
| --- | --- |
| `@jsonName` | Ignored. Properties use member names. |
| `timestamp` | Always epoch seconds. `@timestampFormat` is ignored. |
| `bigInteger`, `bigDecimal` | JSON strings, such as `"9223372036854775808"` |

## Modeling

Apply `@rpcv2Json` to the service. Operations do not use `@http`:

```smithy title="model/weather.smithy" {5,7}
$version: "2"

namespace example.weather

use smithy.protocols#rpcv2Json

@rpcv2Json
service Weather {
    version: "2026-01-01"
    operations: [GetCity]
}
```

A service can declare both `@rpcv2Cbor` and `@rpcv2Json`. Both protocols use
the same routes, so a server maps each on its own endpoint route builder.

## On the wire

```http
POST /service/Weather/operation/GetCity HTTP/1.1
Host: api.example.com
Smithy-Protocol: rpc-v2-json
Content-Type: application/json
Accept: application/json

{"cityId":"123"}

HTTP/1.1 400 Bad Request
Smithy-Protocol: rpc-v2-json
Content-Type: application/json

{"__type":"example.weather#NoSuchResource","resourceType":"city"}
```

## Streaming

Event streams use the same framing as rpcv2Cbor. Event payloads and the
initial-request and initial-response messages are JSON.

## Packages

| Surface | Packages |
| --- | --- |
| Client | `NSmithy.Client`, `NSmithy.Codecs.Json`, `NSmithy.Protocols.RpcV2Json` |
| Server | `NSmithy.Server.AspNetCore`, `NSmithy.Codecs.Json`, `NSmithy.Protocols.RpcV2Json` |

## Specification and tests

- [Smithy RPC v2 JSON specification](https://smithy.io/2.0/additional-specs/protocols/smithy-rpc-v2-json.html)
- [Official rpcv2Json protocol tests](https://github.com/smithy-lang/smithy/tree/main/smithy-protocol-tests/model/rpcv2Json)
