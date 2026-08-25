---
title: REST JSON
description: Compare alloy#simpleRestJson and aws.protocols#restJson1, two JSON over HTTP protocols with Smithy REST bindings.
---

NSmithy supports two REST JSON protocols. Both generate a typed .NET client and
an ASP.NET Core minimal API server, and both use Smithy HTTP binding traits.
Their simplest operations can look identical on the wire, but the protocols are
not equivalent.

## Which protocol should I use?

Choose `restJson1` for most new services. It has a broader wire contract, a
larger interoperability surface, protocol-defined streaming, and AWS-compatible
error handling.

Choose `simpleRestJson` when you need compatibility with
[Alloy](https://github.com/disneystreaming/alloy) or
[Smithy4s](https://disneystreaming.github.io/smithy4s/), or when your model uses
Alloy JSON features such as `@discriminated` and `@jsonUnknown`.

| Protocol | Trait | Best fit |
| --- | --- | --- |
| AWS restJson1 | `aws.protocols#restJson1` | General REST APIs, broad Smithy tooling support, streaming, or AWS-compatible services |
| simpleRestJson | `alloy#simpleRestJson` | Alloy and Smithy4s interoperability, with a deliberately smaller JSON-only wire contract |

Coverage and maturity are tracked on the [Protocol
Status](/smithy-dotnet/protocols/status/) page.

## Key differences

| Area | simpleRestJson | restJson1 |
| --- | --- | --- |
| HTTP bindings | Standard Smithy REST bindings | Standard Smithy REST bindings |
| Structured bodies | JSON | JSON |
| `@httpPayload` | JSON values, including JSON-encoded strings | JSON structures and documents, plus raw strings and blobs with media-type-aware content types |
| Streaming | Not defined by the Alloy protocol | Streaming blobs and Amazon Event Stream input, output, and duplex operations |
| Request body controls | JSON body rules | `@requestCompression` and `@httpChecksumRequired` |
| Error type | `X-Error-Type`, with `__type` accepted by clients | `X-Amzn-Errortype`, plus compatible `__type` and `code` body fields |
| Error compatibility | Normalizes common namespace and qualifier forms | Accepts more discriminator locations and normalizes AWS namespace and qualifier forms |
| JSON traits | Alloy traits including `@discriminated` and `@jsonUnknown` | Standard Smithy and AWS JSON rules |

The shared part is useful but small: ordinary structure members are serialized
as JSON, and the standard HTTP traits decide what moves into the URI, query
string, headers, status code, or payload. restJson1 adds the transport behavior
needed by a wider range of services.

## Modeling

Apply the protocol trait to the service and `@http` to each operation. The
example uses `restJson1`:

```smithy
$version: "2"

namespace example.weather

use aws.protocols#restJson1

@restJson1
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

For `simpleRestJson`, replace the import and service trait with
`alloy#simpleRestJson` and `@simpleRestJson`. That swap is safe while the model
uses only the shared feature set.

Members without an explicit HTTP binding are serialized in the JSON body.

| Trait | Location |
| --- | --- |
| `@httpLabel` | URI path segment |
| `@httpQuery` | Query string |
| `@httpQueryParams` | Open-ended query string parameters |
| `@httpHeader` | Request or response header |
| `@httpPrefixHeaders` | Headers with a modeled prefix |
| `@httpResponseCode` | Response status code |
| `@httpPayload` | Entire request or response body |

## Ordinary requests

For an operation that only uses the shared feature set, both protocols produce
the same request and response:

```http
GET /cities/123 HTTP/1.1
Host: api.example.com
Accept: application/json

HTTP/1.1 200 OK
Content-Type: application/json

{"name":"Seattle"}
```

This equivalence does not extend to raw payloads, event streams, request body
modifiers, or error discrimination.

## restJson1 payloads and streaming

restJson1 supports more than JSON object bodies:

- String payloads use raw UTF-8 text and `text/plain` by default.
- Blob payloads use raw bytes and `application/octet-stream` by default.
- `@mediaType` overrides the content type for an opaque payload.
- `@streaming` blob payloads flow through the client and server without being
  buffered in memory.
- Event stream inputs, outputs, and duplex operations use Amazon Event Stream
  framing with `application/vnd.amazon.eventstream`.
- `@requestCompression` and `@httpChecksumRequired` can compress or checksum a
  buffered request body.

## Error handling

A simpleRestJson error carries its modeled shape name in `X-Error-Type`.

restJson1 servers write `X-Amzn-Errortype`. Clients also accept the error type
from `__type` or `code` in a JSON response body. NSmithy removes namespace and
qualifier forms used by AWS services before matching the value to a generated
error type. This makes restJson1 clients tolerant of the error formats found
across AWS and AWS-compatible services.

## Dependencies

Add the model package for the selected protocol to `smithy-build.json`:

| Protocol | Maven dependency |
| --- | --- |
| simpleRestJson | `com.disneystreaming.alloy:alloy-core:0.3.38` |
| restJson1 | `software.amazon.smithy:smithy-aws-traits:1.73.0` |

| Surface | Packages |
| --- | --- |
| Client | `NSmithy.Client`, `NSmithy.Codecs.Json`, `NSmithy.Protocols.RestJson` |
| Server | `NSmithy.Server.AspNetCore` |

The server package includes the REST JSON protocol and JSON codec transitively.

## Generated API

Both protocols generate the same application-facing API: a typed client and one
handler interface per service. The selected protocol controls routing,
serialization, streaming, and error dispatch without changing handler code.
See the [Protocols Overview](/smithy-dotnet/protocols/overview/) for the client
and server pattern.

## Calling AWS services

restJson1 is useful outside AWS. When calling AWS itself, the request normally
also needs SigV4 signing, regional endpoint resolution, credentials, retries,
and service-specific endpoint rules. NSmithy provides early SigV4 support and a
standard credential chain, but it does not yet cover the complete AWS SDK
runtime. See [Authentication](/smithy-dotnet/guides/client-configuration/authentication/)
and the [AWS Protocols Overview](/smithy-dotnet/protocols/aws-overview/).

## Examples

- [Unary restJson1 weather service](https://github.com/thomaslaich/smithy-dotnet/tree/main/examples/restjson1)
- [Streaming restJson1 chat service](https://github.com/thomaslaich/smithy-dotnet/tree/main/examples/restjson1-streaming)
- [simpleRestJson pizza service](https://github.com/thomaslaich/smithy-dotnet/tree/main/examples/simplerestjson)

## Specifications and tests

- [AWS restJson1 specification](https://smithy.io/2.0/aws/protocols/aws-restjson1-protocol.html)
- [Official restJson1 protocol test models](https://github.com/smithy-lang/smithy/tree/main/smithy-aws-protocol-tests/model/restJson1)
- [Alloy repository](https://github.com/disneystreaming/alloy)
- [Smithy4s simpleRestJson documentation](https://disneystreaming.github.io/smithy4s/docs/protocols/simple-rest-json/overview/)
