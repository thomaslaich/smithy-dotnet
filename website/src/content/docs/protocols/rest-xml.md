---
title: AWS restXml
description: aws.protocols#restXml — XML over HTTP. Client-only, early preview.
---

`aws.protocols#restXml` is the XML-over-HTTP protocol used by AWS services such
as S3 and Route 53. NSmithy generates a typed client that encodes request bodies
as XML and decodes XML responses. Status: **Early preview, client-only**.

The passing slice is weighted toward response deserialization. Request binding
coverage is still narrow and is expected to grow behind the conformance
allowlist.

See [Protocol Status](/smithy-dotnet/protocols/status/) for current conformance
numbers.

## Maven Dependency

```json
"software.amazon.smithy:smithy-aws-traits:1.56.0"
```

## NuGet Packages

| Purpose | Packages |
| --- | --- |
| Client | `NSmithy.Client`, `NSmithy.Protocols.RestXml` |

`NSmithy.Protocols.RestXml` pulls in `NSmithy.Codecs.Xml` (the XML codec)
transitively.

## Modeling

Apply `@restXml` to the service and `@http` to each operation, the same as
`simpleRestJson`. Use `@xmlName` to override the XML element name for a member:

```smithy
$version: "2"

namespace example.weather

use aws.protocols#restXml

@restXml
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
        @xmlName("Name")
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

`@xmlName` overrides the element name in the serialized XML. Without it the
member name is used as-is.

HTTP binding traits (`@httpLabel`, `@httpQuery`, `@httpHeader`, `@httpPayload`)
work the same way as in `simpleRestJson` — members without an explicit binding go
into the XML body. See the [Modeling guide](/smithy-dotnet/guides/modeling/) for
the full set.

## On the Wire

`GetCity` binds `cityId` to the URI path label; the response body is XML. The
`@xmlName("Name")` on the output member sets its element name:

```http
GET /cities/123 HTTP/1.1
Host: api.example.com
Accept: application/xml

HTTP/1.1 200 OK
Content-Type: application/xml

<GetCityOutput><Name>Seattle</Name></GetCityOutput>
```

Members without an HTTP binding are carried in the XML body.

## Usage

The XML codec is wired up automatically by the generated client, which is used
exactly like every other NSmithy client — see [Client & Server
Usage](/smithy-dotnet/protocols/usage/). Only the `@restXml` trait and the XML
wire format are specific to this protocol. NSmithy does not generate restXml
servers.

Explicit SigV4 signing exists in early preview; see
[Authentication](/smithy-dotnet/guides/client-configuration/authentication/). For production calls to
AWS XML services such as S3, prefer the official AWS SDK for .NET until NSmithy's
AWS auth, endpoint resolution, retries, and pagination support mature.
