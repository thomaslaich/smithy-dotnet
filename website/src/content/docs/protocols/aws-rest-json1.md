---
title: AWS restJson1
description: aws.protocols#restJson1 — AWS REST/JSON with Smithy HTTP bindings.
---

`aws.protocols#restJson1` is AWS's REST/JSON protocol. NSmithy generates both a
typed .NET client and an ASP.NET Core minimal API server adapter. Status:
**Preview**.

Although the trait lives under `aws.protocols`, `restJson1` is useful for
non-AWS services too. It has broad Smithy ecosystem support and is the protocol
to choose when you want OpenAPI generation through `smithy-openapi`.

See [Protocol Status](/smithy-dotnet/protocols/status/) for current conformance
numbers.

## Maven Dependency

```json
"software.amazon.smithy:smithy-aws-traits:1.71.0"
```

## NuGet Packages

| Purpose | Package |
| --- | --- |
| Client | `NSmithy.Client` |
| Server (ASP.NET Core) | `NSmithy.Server.AspNetCore` + `Microsoft.AspNetCore.App` |

## Modeling

Use `@restJson1` on the service and Smithy's standard HTTP binding traits on
operations and members:

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

Members without an explicit HTTP binding are serialized in the JSON body.
`restJson1` also carries AWS-specific behavior such as raw string/blob payloads,
`@requestCompression`, `@httpChecksumRequired`, and AWS-style error
deserialization.

## Usage

The generated `IWeatherServiceHandler` and `WeatherClient` are identical to every
other HTTP-JSON/CBOR protocol — see [Client & Server
Usage](/smithy-dotnet/protocols/usage/) for the handler and client code. Only the
`@restJson1` trait and the wire format above are specific to this protocol.

## AWS notes

Explicit SigV4 signing exists in early preview; see
[Authentication](/smithy-dotnet/guides/client-configuration/authentication/). NSmithy does not yet
provide AWS SDK-style endpoint resolution, credential chains, retries, or
pagination helpers. Use the official AWS SDK for .NET for production calls to
AWS services.
