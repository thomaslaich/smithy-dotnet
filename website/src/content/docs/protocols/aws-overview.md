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

If your goal is to call AWS services in production with `restXml` or AWS JSON,
you should use the **[AWS SDK for .NET](https://github.com/aws/aws-sdk-net)**
instead:

- It is officially supported and maintained by AWS.
- It covers the full breadth of AWS services.
- It includes authentication (SigV4/SigV4a), retries, endpoint resolution,
  pagination helpers, and presigned URLs.

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
  in early preview, but AWS SDK-style endpoint resolution, credential chains,
  retries, and pagination helpers are not there yet.

Until those pieces exist, use the official SDK when targeting AWS directly.

## AWS SigV4 Is Early Preview

`NSmithy.Aws` includes explicit SigV4 signing through `AwsSigV4AuthScheme`.
Callers provide the endpoint, signing service name, region, and credentials
provider. This is enough for LocalStack examples and narrow smoke tests, but it
is not a production AWS auth stack.

See [Authentication](/smithy-dotnet/guides/client-configuration/authentication/) for configuration
details and current limitations.

## AWS JSON Is Client-Only

NSmithy includes early client runtime support for `aws.protocols#awsJson1_1` and
`aws.protocols#awsJson1_0`. The current conformance project exercises an initial
`awsJson1_1` slice: target/header construction, empty input/output behavior,
special floating-point values, and client-side error discrimination.

There is no AWS JSON server generation, and production AWS concerns such as
AWS SDK-style credential resolution, endpoint resolution, retries, and pagination
helpers are still outside the generated client.
