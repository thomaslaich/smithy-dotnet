---
title: AWS JSON
description: Client support for the aws.protocols#awsJson1_0 and aws.protocols#awsJson1_1 JSON RPC protocols.
---

`aws.protocols#awsJson1_0` and `aws.protocols#awsJson1_1` are JSON RPC
protocols used by AWS services such as DynamoDB. NSmithy generates typed clients
for both. Server generation and event streaming are not available.

Use AWS JSON to call an existing AWS JSON RPC service or a compatible emulator.
For a new service, prefer [restJson1](../rest-json/),
[rpcv2Cbor](../rpc-v2-cbor/), or [gRPC](../grpc/).

See [Protocol Status](../status/) for maturity and current conformance numbers.

## Protocol behavior

| Area | AWS JSON |
| --- | --- |
| Route | `POST /` |
| Operation | `X-Amz-Target: {Service}.{Operation}` |
| Body | JSON |
| JSON 1.0 content type | `application/x-amz-json-1.0` |
| JSON 1.1 content type | `application/x-amz-json-1.1` |
| Errors | `X-Amzn-Errortype`, `__type`, `code`, or HTTP status fallback |
| HTTP bindings | Not used |
| NSmithy streaming | Not implemented |

## Modeling

Apply one AWS JSON protocol trait to the service. Operations do not use `@http`
because the protocol always posts to the root path.

```smithy
$version: "2"

namespace example.weather

use aws.protocols#awsJson1_1

@awsJson1_1
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
    message: String
}
```

Use `aws.protocols#awsJson1_0` and `@awsJson1_0` for a JSON 1.0 service.

## On the wire

```http
POST / HTTP/1.1
Host: api.example.com
Content-Type: application/x-amz-json-1.1
Accept: application/x-amz-json-1.1
X-Amz-Target: Weather.GetCity

{"cityId":"123"}

HTTP/1.1 200 OK
Content-Type: application/x-amz-json-1.1

{"name":"Seattle"}
```

The service and operation names select the target. Empty inputs serialize as an
empty JSON object. Clients accept empty output bodies when the modeled output can
be constructed without response members.

For errors, NSmithy accepts the modeled type from `X-Amzn-Errortype`, `__type`,
or `code`. It removes common namespace and qualifier forms before matching a
generated error. If a response has no discriminator, the client can fall back to
the modeled HTTP status code.

## Dependencies

Add the AWS trait package to `smithy-build.json`:

```json
"software.amazon.smithy:smithy-aws-traits:1.73.0"
```

The client uses `NSmithy.Client`, `NSmithy.Codecs.Json`, and
`NSmithy.Protocols.AwsJson`.

## Calling AWS

AWS endpoints normally require SigV4, regional endpoint resolution, and
credentials. See
[Authentication](/smithy-dotnet/guides/client-configuration/authentication/)
for the NSmithy setup and [AWS Protocols](../aws-overview/) for current runtime
gaps.

## Example

The [AWS LocalStack
example](https://github.com/thomaslaich/smithy-dotnet/tree/main/examples/aws-localstack)
uses an AWS JSON 1.0 client to call DynamoDB `ListTables`. NSmithy does not
implement AWS JSON streaming yet, so there is no streaming example.

## Specification and tests

- [AWS JSON 1.0 specification](https://smithy.io/2.0/aws/protocols/aws-json-1_0-protocol.html)
- [AWS JSON 1.1 specification](https://smithy.io/2.0/aws/protocols/aws-json-1_1-protocol.html)
- [Official AWS JSON protocol tests](https://github.com/smithy-lang/smithy/tree/main/smithy-aws-protocol-tests/model/awsJson1_1)
