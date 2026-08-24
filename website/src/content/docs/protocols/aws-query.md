---
title: AWS Query
description: Form-encoded requests and XML responses using aws.protocols#awsQuery.
---

`aws.protocols#awsQuery` is an RPC protocol that sends form-encoded requests
and receives XML responses. NSmithy generates typed clients. Server generation
and streaming are not available.

:::caution[Existing services only]
Smithy deprecates AWS Query for new services. Use it for existing AWS services
and compatible local emulators.
:::

See [Protocol Status](../status/) for maturity and current conformance numbers.

## Protocol behavior

| Area | AWS Query |
| --- | --- |
| Route | `POST /` |
| Request | `application/x-www-form-urlencoded` |
| Operation | `Action={Operation}` |
| Version | `Version={service version}` |
| Response | XML with an `{Operation}Result` wrapper |
| Errors | AWS Query XML error envelope and optional `@awsQueryError` |
| HTTP bindings | Not used |
| Streaming | Not supported |

AWS Query does not support document shapes. Lists and maps use dotted form keys,
and XML traits control names and collection flattening.

## Modeling

Apply `@awsQuery` and `@xmlNamespace` to the service:

```smithy
$version: "2"

namespace example.queue

use aws.protocols#awsQuery

@awsQuery
@xmlNamespace(uri: "https://queue.example.com/doc/2026-01-01/")
service QueueService {
    version: "2026-01-01"
    operations: [ListQueues]
}

operation ListQueues {
    input := {
        namePrefix: String
    }
    output := {
        @xmlFlattened
        @xmlName("QueueUrl")
        queueUrls: QueueUrlList
    }
}

list QueueUrlList {
    member: String
}
```

`@xmlName` changes form keys and XML element names. `@xmlFlattened` removes
the normal list or map wrapper. An error structure can use
`aws.protocols#awsQueryError` to set its wire error code and HTTP status.

## On the wire

```http
POST / HTTP/1.1
Host: queue.example.com
Content-Type: application/x-www-form-urlencoded
Accept: text/xml

Action=ListQueues&Version=2026-01-01&namePrefix=jobs

HTTP/1.1 200 OK
Content-Type: text/xml

<ListQueuesResponse xmlns="https://queue.example.com/doc/2026-01-01/">
  <ListQueuesResult>
    <QueueUrl>https://queue.example.com/jobs-1</QueueUrl>
  </ListQueuesResult>
  <ResponseMetadata><RequestId>abc</RequestId></ResponseMetadata>
</ListQueuesResponse>
```

Lists use `member` segments by default. Maps use numbered `entry`, `key`,
and `value` segments. Successful output members are nested inside
`{Operation}Result`.

## Dependencies

Add the AWS trait package to `smithy-build.json`:

```json
"software.amazon.smithy:smithy-aws-traits:1.73.0"
```

The client uses `NSmithy.Client` and `NSmithy.Protocols.AwsQuery`. The
protocol package includes the XML codec transitively.

## Calling AWS

AWS endpoints normally require SigV4, regional endpoint resolution, and
credentials. See
[Authentication](/smithy-dotnet/guides/client-configuration/authentication/)
for the NSmithy setup and [AWS Protocols](../aws-overview/) for current runtime
gaps.

## Example

The [AWS LocalStack
example](https://github.com/thomaslaich/smithy-dotnet/tree/main/examples/aws-localstack)
uses an AWS Query client to call SQS `ListQueues`. AWS Query does not support
streaming, so there is no streaming example.

## Specification and tests

- [AWS Query specification](https://smithy.io/2.0/aws/protocols/aws-query-protocol.html)
- [Official AWS Query protocol tests](https://github.com/smithy-lang/smithy/tree/main/smithy-aws-protocol-tests/model/awsQuery)
