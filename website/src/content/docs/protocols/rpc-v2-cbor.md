---
title: RPC v2 CBOR
description: smithy.protocols#rpcv2Cbor — binary CBOR encoding over HTTP. Client and server support.
---

`smithy.protocols#rpcv2Cbor` is Smithy's binary protocol. Messages are encoded
as [CBOR](https://cbor.io/) and carried over HTTP POST requests on a fixed
path derived from the service and operation names. Status: **Preview**.

See [Protocol Status](/smithy-dotnet/protocols/status/) for current conformance
numbers.

## Maven Dependency

No extra Maven dependency beyond the codegen plugin — `smithy.protocols` shapes
are bundled with the Smithy CLI.

## NuGet Packages

| Purpose | Packages |
| --- | --- |
| Client | `NSmithy.Client`, `NSmithy.Codecs.Cbor`, `NSmithy.Protocols.RpcV2Cbor` |
| Server (ASP.NET Core) | `NSmithy.Server.AspNetCore`, `NSmithy.Codecs.Cbor`, `NSmithy.Protocols.RpcV2Cbor` |

## Modeling

Apply `@rpcv2Cbor` to the service. Operations do not carry `@http` traits —
the protocol maps each operation to a fixed
`POST /service/{Service}/operation/{Operation}` path automatically:

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

## Usage

The generated handler and client are identical to every other HTTP-JSON/CBOR
protocol — see [Client & Server Usage](/smithy-dotnet/protocols/usage/). The CBOR
codec is wired up automatically; the only thing specific to this protocol is the
`@rpcv2Cbor` trait and the binary wire format on the fixed operation path.

## Example

A complete working server+client example is available in
[`examples/rpcv2cbor`](https://github.com/thomaslaich/smithy-dotnet/tree/main/examples/rpcv2cbor).
