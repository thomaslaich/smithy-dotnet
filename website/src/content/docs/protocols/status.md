---
title: Protocol Status
description: Maturity and official conformance coverage for every NSmithy protocol.
---

This page is the source of truth for protocol maturity and conformance. The
individual protocol pages describe behavior without repeating status labels or
test totals.

## Current status

| Protocol | Surfaces | Stage | Client conformance | Server conformance |
| --- | --- | --- | --- | --- |
| [`aws.protocols#restJson1`](../rest-json/) | Client and server | Preview | Requests 142/142 (100%), responses 108/108 (100%) | Requests 132/132 (100%), responses 92/92 (100%), malformed 655/655 (100%) |
| [`aws.protocols#awsJson1_1`](../aws-json/) | Client | Early preview | Requests 57/57 (100%), responses 62/62 (100%) | N/A |
| [`aws.protocols#awsJson1_0`](../aws-json/) | Client | Early preview | Runtime support, no conformance project | N/A |
| [`aws.protocols#awsQuery`](../aws-query/) | Client | Preview | Requests 38/38 (100%), responses 39/39 (100%) | N/A |
| [`aws.protocols#ec2Query`](../aws-ec2-query/) | Client | Preview | Requests 30/30 (100%), responses 29/29 (100%) | N/A |
| [`aws.protocols#restXml`](../rest-xml/) | Client | Preview | Requests 109/109 (100%), responses 84/84 (100%) | N/A |
| [`smithy.protocols#rpcv2Cbor`](../rpc-v2-cbor/) | Client and server | Preview | Requests 29/29 (100%), responses 43/43 (100%) | Requests 37/37 (100%), responses 27/27 (100%) |
| [`alloy#simpleRestJson`](../rest-json/) | Client and server | Preview | Requests 23/23 (100%), responses 20/20 (100%) | Requests 23/23 (100%), responses 20/20 (100%) |
| [`alloy.proto#grpc`](../grpc/) | Client and server | Experimental | End-to-end examples | End-to-end examples |
| [`bote#kafkaJson`](../bote-kafka-json/) | Producer and consumers | Experimental | End-to-end examples | N/A |

## How the numbers are counted

Each fraction is:

```text
passing executable official cases / official cases applicable to that surface
```

Client and server totals are separate because Smithy test cases declare an
`appliesTo` direction. A server-only case does not enter the client denominator,
and a client-only case does not enter the server denominator.

Cases listed in a conformance project's known-gap set remain in the denominator
but not the numerator. Local regression fixtures run in the suites but do not
count as official conformance.

## Coverage notes

- restJson1 passes every applicable official request, response, modeled-error,
  and malformed-request case on both generated surfaces.
- The restJson1 server passes all 655 applicable
  `httpMalformedRequestTests`. These cases verify structured responses for
  requests that cannot be deserialized or validated.
- Auxiliary fixture services without a generated handler are excluded from the
  restJson1 server denominator. Local response regression cases are also
  excluded from the official totals.
- simpleRestJson passes every case in Alloy's protocol suite on both generated
  surfaces.
- rpcv2Cbor passes every applicable official request and response case on both
  generated surfaces.
- AWS Query, EC2 Query, and restXml pass every applicable official client
  request and response case.
- AWS JSON 1.1 passes every applicable official client request and response case.
  `AwsJson10Protocol` has runtime support but no separate conformance project.
- gRPC is not part of Smithy's HTTP protocol test suite. Its codec and transport
  tests cover complex messages, numeric encodings, collections, oneofs,
  documents, errors, trailers, and all three streaming modes. Grpc.Net
  interoperability is demonstrated by runnable examples but is not yet an
  automated cross-implementation test matrix.
- kafkaJson is a messaging protocol without a client/server split or applicable
  Smithy HTTP conformance suite. The generated producer and consumers are
  validated by the end-to-end `examples/kafka` project against a real broker.

See [Validation](/smithy-dotnet/servers/validation/) for malformed request
behavior.

## Maturity labels

- **Preview:** useful end-to-end support with meaningful conformance coverage.
  Some protocol features or convenience APIs are still incomplete.
- **Early preview:** a narrower verified slice. Check the conformance totals and
  known limitations before adopting it.
- **Experimental:** the API and supported model surface can still change.

## Reproducing the report

Each conformance project has a
`ConformanceRateTests.ReportConformanceRate` test. Run it after `just codegen`
and copy the emitted totals to this page.

The fixtures come from the pinned Smithy and Alloy protocol-test model JARs.
The upstream restJson1 sources are in the [Smithy
repository](https://github.com/smithy-lang/smithy/tree/main/smithy-aws-protocol-tests/model/restJson1).
