---
title: AWS JSON
description: aws.protocols#awsJson1_1 and aws.protocols#awsJson1_0 — client-only JSON RPC for AWS-compatible services.
---

`aws.protocols#awsJson1_1` and `aws.protocols#awsJson1_0` are AWS JSON-RPC
protocols. NSmithy generates typed clients for both through
`AwsJson11Protocol` and `AwsJson10Protocol`. Status: **Early preview,
client-only**.

The currently verified slice covers `X-Amz-Target`, AWS JSON content types,
empty input/output handling, special floating-point values, and common
client-side error discriminator formats. `awsJson1_0` has runtime support but no
separate conformance project yet.

See [Protocol Status](/smithy-dotnet/protocols/status/) for current conformance
numbers.

## Maven Dependency

```json
"software.amazon.smithy:smithy-aws-traits:1.71.0"
```

## NuGet Packages

| Purpose | Packages |
| --- | --- |
| Client | `NSmithy.Client`, `NSmithy.Protocols.AwsJson` |

`NSmithy.Protocols.AwsJson` pulls in `NSmithy.Codecs.Json` (the JSON codec)
transitively.

## Modeling

Apply the AWS JSON protocol trait to the service:

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
    @required
    resourceType: String
}
```

## On the Wire

AWS JSON is RPC-style: every call is a `POST /`, the operation is selected by the
`X-Amz-Target` header, and input/output are JSON:

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

`X-Amz-Target` is `{Service}.{Operation}` (`awsJson1_0` uses the
`application/x-amz-json-1.0` content type). Errors are discriminated by the
`X-Amzn-Errortype` header or a `__type` field in the body.

## Usage

The generated client selects the declared protocol by default and is used
exactly like every other NSmithy client — see [Client & Server
Usage](/smithy-dotnet/protocols/usage/) for the client code. Only the
`@awsJson1_1` (or `@awsJson1_0`) trait is specific to this protocol.

AWS JSON support is client-only. NSmithy does not generate AWS JSON servers and
does not yet provide AWS SDK-style endpoint resolution, credential chains,
retries, or pagination helpers. Explicit SigV4 signing exists in early preview;
see [Authentication](/smithy-dotnet/guides/client-configuration/authentication/).
