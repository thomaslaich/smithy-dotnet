---
title: AWS Protocols
description: Choose an AWS wire protocol and understand the runtime features needed to call AWS services.
---

NSmithy supports several wire protocols used by AWS services. AWS JSON, AWS
Query, EC2 Query, and restXml are primarily useful for calling those services or
compatible emulators such as LocalStack. restJson1 is the exception: it is also
a practical protocol for APIs outside AWS.

Protocol support is separate from the rest of the AWS SDK runtime. A successful
production AWS client also needs authentication, endpoint rules, retries,
credentials, and service-level conveniences.

## Supported AWS protocols

| Protocol | Generated surfaces | Typical use |
| --- | --- | --- |
| [restJson1](../rest-json/) | Client and server | REST APIs, event streams, and AWS-compatible services |
| [awsJson1_0 and awsJson1_1](../aws-json/) | Client | JSON RPC services such as DynamoDB |
| [awsQuery](../aws-query/) | Client | Existing AWS Query services such as SQS |
| [ec2Query](../aws-ec2-query/) | Client | EC2 Query services |
| [restXml](../rest-xml/) | Client | XML services such as S3 |

See [Protocol Status](../status/) for maturity and conformance numbers.

## New services

`restJson1` is useful beyond AWS and is a strong default for a new REST API.
It has broad Smithy tooling support, standard HTTP bindings, raw and streaming
payloads, Amazon Event Stream, request compression, checksums, and
AWS-compatible error handling.

The other AWS protocols exist mainly for compatibility with established
services. Smithy deprecates AWS Query for new service design.

## Calling AWS from .NET

If you want to use AWS from .NET, you most likely want the official [AWS SDK
for .NET](https://github.com/aws/aws-sdk-net). It is the supported client for the
full AWS service catalog and includes the complete set of service-specific
features.

NSmithy is not intended to replace the AWS SDK. It does, however, cover a useful
part of the AWS client stack:

- SigV4 signing and presigning
- Standard regional endpoint resolution
- Environment, shared-profile, SSO, and IMDS credentials
- Standard retries with jitter, retry quotas, `Retry-After`, and modeled
  `@retryable` errors
- Generated page and item paginators for modeled `@paginated` operations
- Clients for the supported AWS protocols when supplied with a Smithy model

NSmithy does not yet provide:

- SigV4a
- Modeled service-specific endpoint rule sets
- The complete credential provider chain, including assume-role, web identity,
  and ECS container credentials
- AWS service-specific retry, paginator, and utility behavior
- Maintained coverage for the full AWS service catalog or official AWS support

This makes NSmithy useful for focused integrations, emulators, and clients built
from your own Smithy models. See
[Authentication](/smithy-dotnet/guides/client-configuration/authentication/),
[Retry](/smithy-dotnet/guides/client-configuration/retry/), and
[Pagination](/smithy-dotnet/guides/client-configuration/pagination/) for details.

## AWS as a proving ground

AWS is also a demanding test bed for NSmithy. The official AWS protocol test
suites exercise a wide range of HTTP bindings, data shapes, error responses,
and edge cases. LocalStack and real service calls add interoperability coverage
beyond the fixtures.

Supporting these protocols helps find weaknesses in serialization, transport,
authentication, and generated clients. Those improvements also harden the same
runtime used for non-AWS Smithy services, especially `restJson1` services.

## Examples

The [AWS LocalStack
example](https://github.com/thomaslaich/smithy-dotnet/tree/main/examples/aws-localstack)
uses generated clients for all five supported AWS HTTP protocols:

- DynamoDB over AWS JSON 1.0
- S3 over restXml
- Lambda over restJson1
- SQS over AWS Query
- EC2 over EC2 Query

For restJson1 event streams, see the [streaming
example](https://github.com/thomaslaich/smithy-dotnet/tree/main/examples/restjson1/streaming).
