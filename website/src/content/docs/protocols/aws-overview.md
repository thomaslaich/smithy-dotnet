---
title: AWS Protocols Overview
description: Important context before using NSmithy's AWS protocol support.
---

NSmithy's AWS protocol support exists primarily as a **proof of concept** — a
vehicle for validating the generator's protocol abstraction, codec layer, and
conformance test infrastructure against real AWS protocol definitions. This is
especially true for `aws.protocols#restXml` and the AWS JSON protocols.

AWS restJson1 is the exception — see below.

(`smithy.protocols#rpcv2Cbor` is a Smithy-standard binary protocol, not AWS-specific;
it is documented as a top-level protocol rather than under this AWS section.)

## Use the Official AWS SDK for .NET Instead

If your goal is to call AWS services in production with an AWS protocol,
you should use the **[AWS SDK for .NET](https://github.com/aws/aws-sdk-net)**
instead:

- It is officially supported and maintained by AWS.
- It covers the full breadth of AWS services.
- It includes authentication (SigV4/SigV4a), retries, endpoint resolution,
  pagination helpers, and the complete set of service-specific endpoint rules.

## AWS restJson1 Is Different

`aws.protocols#restJson1` is not AWS-specific in practice — it is a
well-defined REST/JSON wire format usable by any HTTP service, whether or not
it runs on AWS. Many teams use it to define internal or public APIs that follow
the same protocol as AWS services. For that reason it is documented as a
top-level protocol, alongside `simpleRestJson`, on the [REST
JSON](/smithy-dotnet/protocols/rest-json/) page rather than under this AWS
section.

For AWS restJson1, NSmithy targets more than proof-of-concept use:

- **Non-AWS services** — generated clients and servers work today and are a
  reasonable choice for services modelled with `restJson1` outside of AWS.
- **AWS services in production** — the goal is for NSmithy-generated `restJson1`
  clients to be usable against real AWS services. Explicit SigV4 signing exists
  in preview with regional endpoint resolution, profile/SSO/IMDS credentials,
  and presigning, but service-specific endpoint rules, the full credential
  chain, retries, and pagination helpers are not there yet.

Until those pieces exist, use the official SDK when targeting AWS directly.

## AWS SigV4 Is Early Preview

`NSmithy.Aws` includes explicit SigV4 signing through `AwsSigV4AuthScheme`.
Callers provide the signing service name and region. The package includes a
standard regional endpoint resolver, a default environment/profile/SSO/IMDS
credential chain, and presigning. This is enough for LocalStack and focused AWS
integrations, but it is not yet the complete production stack of the official SDK.

See [Authentication](/smithy-dotnet/guides/client-configuration/authentication/) for configuration
details and current limitations.

## AWS JSON Is Client-Only

NSmithy includes early client runtime support for `aws.protocols#awsJson1_1` and
`aws.protocols#awsJson1_0`. The current conformance project exercises an initial
`awsJson1_1` slice: target/header construction, empty input/output behavior,
special floating-point values, and client-side error discrimination.

There is no AWS JSON server generation, and production AWS concerns such as
modeled endpoint rule sets, additional credential sources, retries, and
pagination helpers are still outside the generated client.

## AWS Query Protocols Are Client-Only

`aws.protocols#awsQuery` and `aws.protocols#ec2Query` have generated client
support and pass every applicable request and response case in Smithy's official
AWS protocol fixtures. They intentionally do not generate servers.
