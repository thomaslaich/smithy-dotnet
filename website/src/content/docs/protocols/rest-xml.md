---
title: AWS restXml
description: XML over HTTP client support for aws.protocols#restXml.
---

`aws.protocols#restXml` is the XML over HTTP protocol used by AWS services such
as S3 and Route 53. NSmithy generates typed clients with Smithy HTTP bindings,
XML request bodies, and XML response bodies. Server generation and event
streaming are not available.

Use restXml to call an existing AWS XML service or a compatible emulator. For a
new service, prefer [restJson1](../rest-json/),
[rpcv2Cbor](../rpc-v2-cbor/), or [gRPC](../grpc/).

See [Protocol Status](../status/) for maturity and current conformance numbers.

## Protocol behavior

| Area | restXml |
| --- | --- |
| Route and method | Defined by `@http` |
| Body | XML |
| Content type | `application/xml` |
| Member bindings | Standard Smithy HTTP binding traits |
| Errors | Modeled code in an XML `Error` or `ErrorResponse` body |
| Server | Not generated |
| NSmithy streaming | Not implemented |

restXml does not support document shapes.

## Modeling

Apply `@restXml` to the service and `@http` to each operation. XML traits
control element names, namespaces, and collection layout.

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
}
```

Members without an HTTP binding are serialized in the XML body. `@xmlName`
changes an element name, `@xmlNamespace` declares a namespace, `@xmlAttribute`
moves a value to an attribute, and `@xmlFlattened` removes the normal collection
wrapper.

The standard REST binding traits place values in URI labels, query parameters,
headers, prefix headers, response status codes, or a complete `@httpPayload`
body.

## On the wire

```http
GET /cities/123 HTTP/1.1
Host: api.example.com
Accept: application/xml

HTTP/1.1 200 OK
Content-Type: application/xml

<GetCityResponse><Name>Seattle</Name></GetCityResponse>
```

A modeled error is selected from the `Code` element. NSmithy accepts both a
direct `Error` body and the common `ErrorResponse > Error` envelope.

## Dependencies

Add the AWS trait package to `smithy-build.json`:

```json
"software.amazon.smithy:smithy-aws-traits:1.73.0"
```

The client uses `NSmithy.Client`, `NSmithy.Codecs.Xml`, and
`NSmithy.Protocols.RestXml`.

## Calling AWS

AWS endpoints normally require SigV4, regional endpoint resolution, and
credentials. See
[Authentication](/smithy-dotnet/guides/client-configuration/authentication/)
for the NSmithy setup and [AWS Protocols](../aws-overview/) for current runtime
gaps.

## Example

The [AWS LocalStack
example](https://github.com/thomaslaich/smithy-dotnet/tree/main/examples/aws-localstack)
uses a restXml client to call S3 `ListBuckets`. NSmithy does not implement
restXml streaming yet, so there is no streaming example.

## Specification and tests

- [AWS restXml specification](https://smithy.io/2.0/aws/protocols/aws-restxml-protocol.html)
- [Official restXml protocol tests](https://github.com/smithy-lang/smithy/tree/main/smithy-aws-protocol-tests/model/restXml)
