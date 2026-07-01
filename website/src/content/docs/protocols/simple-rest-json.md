---
title: simpleRestJson
description: alloy#simpleRestJson — JSON over HTTP with REST bindings.
---

`alloy#simpleRestJson` is a JSON-over-HTTP protocol with Smithy HTTP bindings.
NSmithy generates both a typed .NET client and an ASP.NET Core minimal API
server adapter. Status: **Preview**.

Use `simpleRestJson` when your consumers are primarily .NET or Scala
(via [Smithy4s](https://disneystreaming.github.io/smithy4s/)) and you want the
smoothest current NSmithy end-to-end path. Use
[`aws.protocols#restJson1`](/smithy-dotnet/protocols/aws-rest-json1/) when you
need broader Smithy ecosystem compatibility or OpenAPI generation.

See [Protocol Status](/smithy-dotnet/protocols/status/) for current conformance
numbers.

## Maven Dependency

```json
"com.disneystreaming.alloy:alloy-core:0.3.38"
```

## NuGet Packages

| Purpose | Package |
| --- | --- |
| Client | `NSmithy.Client` |
| Server (ASP.NET Core) | `NSmithy.Server.AspNetCore` + `Microsoft.AspNetCore.App` |

## Modeling

Apply `@simpleRestJson` to the service and Smithy's standard HTTP binding traits
on operations and members:

```smithy
$version: "2"

namespace example.weather

use alloy#simpleRestJson

@simpleRestJson
service Weather {
    version: "2026-01-01"
    operations: [GetCity]
}

@readonly
@http(method: "GET", uri: "/cities/{cityId}")
operation GetCity {
    input := {
        @required
        @httpLabel
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

Members without an explicit HTTP binding are serialized in the JSON body. For
resources, pagination, and the full set of HTTP binding traits, see the
[Modeling guide](/smithy-dotnet/guides/modeling/).

## Usage

NSmithy generates one `IWeatherServiceHandler` interface with a method per
operation, plus a typed `WeatherClient`. Implement the handler once; the
generated ASP.NET Core minimal API adapter handles routing, serialization, and
error dispatch. This handler-and-client shape is the same across every
HTTP-JSON/CBOR protocol — see [Client & Server
Usage](/smithy-dotnet/protocols/usage/) for the full example. Only the
`@simpleRestJson` trait is specific to this protocol.
