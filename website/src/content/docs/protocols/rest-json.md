---
title: REST JSON
description: alloy#simpleRestJson and aws.protocols#restJson1 — JSON over HTTP with Smithy REST bindings. Client and server support.
---

NSmithy supports two JSON-over-HTTP protocols with Smithy HTTP bindings, and
generates a typed .NET client and an ASP.NET Core minimal-API server for each:

| Trait | Namespace | Status |
| --- | --- | --- |
| `@simpleRestJson` | `alloy#simpleRestJson` | Preview |
| `@restJson1` | `aws.protocols#restJson1` | Preview |

The two are the same shape on the wire — JSON body, Smithy HTTP binding traits,
an error type carried in a response header. They differ only in ecosystem and a
few AWS-specific behaviors.

## Choosing between them

- **`simpleRestJson`** — the [alloy](https://github.com/disneystreaming/alloy)
  variant. Choose it when your consumers are primarily .NET or Scala
  (via [Smithy4s](https://disneystreaming.github.io/smithy4s/)).
- **`restJson1`** — AWS's REST/JSON protocol, but useful for non-AWS services
  too. Choose it when you want broad Smithy ecosystem compatibility or OpenAPI
  generation through `smithy-openapi`. It also supports capabilities
  `simpleRestJson` does not: event streaming (the `vnd.amazon.eventstream`
  framing), raw string/blob payloads, `@requestCompression`,
  `@httpChecksumRequired`, and AWS-style error deserialization.

See [Protocol Status](/smithy-dotnet/protocols/status/) for current conformance
numbers.

## Maven Dependency

| Protocol | Dependency |
| --- | --- |
| `simpleRestJson` | `com.disneystreaming.alloy:alloy-core:0.3.38` |
| `restJson1` | `software.amazon.smithy:smithy-aws-traits:1.71.0` |

## NuGet Packages

| Purpose | Package |
| --- | --- |
| Client | `NSmithy.Client` |
| Server (ASP.NET Core) | `NSmithy.Server.AspNetCore` + `Microsoft.AspNetCore.App` |

## Modeling

Apply the protocol trait to the service and Smithy's standard HTTP binding
traits on operations and members. The model is identical apart from the trait
and its `use` statement — swap `alloy#simpleRestJson` / `@simpleRestJson` for
`aws.protocols#restJson1` / `@restJson1` to switch protocols:

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

Members without an explicit HTTP binding are serialized in the JSON body. The
HTTP binding traits control where each other member lives:

| Trait | Binds member to |
| --- | --- |
| `@httpLabel` | URI path segment |
| `@httpQuery("key")` | query string parameter |
| `@httpHeader("name")` | request or response header |
| `@httpPayload` | raw request/response body |

## On the Wire

`GetCity` binds `cityId` to the URI path label; the response comes back as a JSON
body. The request and response are byte-for-byte the same across both protocols:

```http
GET /cities/123 HTTP/1.1
Host: api.example.com
Accept: application/json

HTTP/1.1 200 OK
Content-Type: application/json

{"name":"Seattle"}
```

Members without an HTTP binding are carried in the JSON body. Errors are
discriminated by a response header — the one difference on the wire:

| Protocol | Error header |
| --- | --- |
| `simpleRestJson` | `X-Error-Type` |
| `restJson1` | `X-Amzn-Errortype` |

## Usage

NSmithy generates one `IWeatherServiceHandler` interface with a method per
operation, plus a typed `WeatherClient`. Implement the handler once; the
generated ASP.NET Core adapter handles routing, serialization, and error
dispatch. This handler-and-client shape is the same across every protocol — the
protocol trait never reaches your code. See the [Protocols
Overview](/smithy-dotnet/protocols/overview/) for the canonical example and
[Servers](/smithy-dotnet/servers/) for the full server walkthrough.

## AWS restJson1 notes

`restJson1` lives under `aws.protocols`, but it is not AWS-specific in practice —
it is a well-defined REST/JSON wire format usable by any HTTP service. For
production calls to real AWS services, explicit SigV4 signing exists in early
preview (see
[Authentication](/smithy-dotnet/guides/client-configuration/authentication/)),
but AWS SDK-style endpoint resolution, credential chains, retries, and pagination
helpers are not there yet — use the official AWS SDK for .NET until they mature.
See the [AWS Protocols Overview](/smithy-dotnet/protocols/aws-overview/) for more
context.
