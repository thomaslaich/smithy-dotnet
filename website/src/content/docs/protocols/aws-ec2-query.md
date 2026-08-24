---
title: AWS EC2 Query
description: Form-encoded EC2 Query requests and XML responses using aws.protocols#ec2Query.
---

`aws.protocols#ec2Query` is the EC2-specific variant of [AWS
Query](../aws-query/). It uses the same form request and XML response pattern,
but changes member names, collection keys, success envelopes, and errors.
NSmithy generates typed clients. Server generation and streaming are not
available.

Use EC2 Query only to call EC2 or an emulator that reproduces its Query
endpoint.

See [Protocol Status](../status/) for maturity and current conformance numbers.

## Protocol behavior

| Area | EC2 Query |
| --- | --- |
| Route | `POST /` |
| Request | `application/x-www-form-urlencoded` |
| Operation | `Action={Operation}` |
| Version | `Version={service version}` |
| Response | XML members directly under the response root |
| Errors | `Response > Errors > Error` XML envelope |
| HTTP bindings | Not used |
| Streaming | Not supported |

EC2 Query does not support document shapes or request maps. Request member names
are capitalized by default, and lists are flattened with one-based indexes.

## Modeling

Apply `@ec2Query` and `@xmlNamespace` to the service. Use `@ec2QueryName`
when a request key differs from the modeled member name.

```smithy
$version: "2"

namespace example.compute

use aws.protocols#ec2Query
use aws.protocols#ec2QueryName

@ec2Query
@xmlNamespace(uri: "https://compute.example.com/doc/2026-01-01/")
service ComputeService {
    version: "2026-01-01"
    operations: [DescribeRegions]
}

operation DescribeRegions {
    input := {
        @ec2QueryName("RegionName")
        regionNames: RegionNameList
    }
    output := {
        @xmlName("regionInfo")
        regions: RegionList
    }
}

list RegionNameList {
    member: String
}

list RegionList {
    @xmlName("item")
    member: Region
}

structure Region {
    @xmlName("regionName")
    name: String
}
```

If `@ec2QueryName` is absent, `@xmlName` supplies the request name and its
first character is capitalized.

## On the wire

```http
POST / HTTP/1.1
Host: compute.example.com
Content-Type: application/x-www-form-urlencoded
Accept: text/xml

Action=DescribeRegions&Version=2026-01-01&RegionName.1=eu-west-1

HTTP/1.1 200 OK
Content-Type: text/xml

<DescribeRegionsResponse xmlns="https://compute.example.com/doc/2026-01-01/">
  <regionInfo>
    <item><regionName>eu-west-1</regionName></item>
  </regionInfo>
  <requestId>abc</requestId>
</DescribeRegionsResponse>
```

Unlike AWS Query, successful output members are read directly from the response
root instead of an `{Operation}Result` wrapper.

## Dependencies

Add the AWS trait package to `smithy-build.json`:

```json
"software.amazon.smithy:smithy-aws-traits:1.73.0"
```

The client uses `NSmithy.Client` and `NSmithy.Protocols.AwsQuery`. The same
protocol package provides both AWS Query implementations and includes the XML
codec transitively.

## Calling AWS

AWS endpoints normally require SigV4, regional endpoint resolution, and
credentials. See
[Authentication](/smithy-dotnet/guides/client-configuration/authentication/)
for the NSmithy setup and [AWS Protocols](../aws-overview/) for current runtime
gaps.

## Example

The [AWS LocalStack
example](https://github.com/thomaslaich/smithy-dotnet/tree/main/examples/aws-localstack)
uses an EC2 Query client to call EC2 `DescribeRegions`. EC2 Query does not
support streaming, so there is no streaming example.

## Specification and tests

- [EC2 Query specification](https://smithy.io/2.0/aws/protocols/aws-ec2-query-protocol.html)
- [Official EC2 Query protocol tests](https://github.com/smithy-lang/smithy/tree/main/smithy-aws-protocol-tests/model/ec2Query)
