---
title: Protocol Conformance Matrix
description: Official protocol conformance test results for Smithy.NET.
---

| Protocol | Case kind | Executable | Skipped | Total | Conformance |
| --- | ---: | ---: | ---: | ---: | ---: |
| `alloy#simpleRestJson` | `Request` | 17 | 6 | 23 | 73.9% |
| `alloy#simpleRestJson` | `Response` | 14 | 6 | 20 | 70.0% |
| `aws.protocols#restJson1` | `Request` | 5 | 153 | 158 | 3.2% |
| `aws.protocols#restJson1` | `Response` | 4 | 110 | 114 | 3.5% |
| `aws.protocols#restJson1` | `MalformedRequest` | 0 | 191 | 191 | 0.0% |

## Executable Cases

- `alloy#simpleRestJson` `Request` `AddMenuItem`
- `alloy#simpleRestJson` `Request` `CustomCodeInput`
- `alloy#simpleRestJson` `Request` `GetEnumInput`
- `alloy#simpleRestJson` `Request` `GetIntEnumInput`
- `alloy#simpleRestJson` `Request` `GetMenuRequest`
- `alloy#simpleRestJson` `Request` `HeaderEndpointInput`
- `alloy#simpleRestJson` `Request` `HealthGet`
- `alloy#simpleRestJson` `Request` `RoundTripRequest`
- `alloy#simpleRestJson` `Request` `RoutingAbc`
- `alloy#simpleRestJson` `Request` `RoutingAbcDef`
- `alloy#simpleRestJson` `Request` `RoutingAbcDefGreedy`
- `alloy#simpleRestJson` `Request` `RoutingAbcLabel`
- `alloy#simpleRestJson` `Request` `RoutingAbcXyz`
- `alloy#simpleRestJson` `Request` `SimpleRestJsonNoneHttpPayloadWithDefault`
- `alloy#simpleRestJson` `Request` `SimpleRestJsonNoneRequiredHttpPayloadWithDefault`
- `alloy#simpleRestJson` `Request` `SimpleRestJsonSomeHttpPayloadWithDefault`
- `alloy#simpleRestJson` `Request` `SimpleRestJsonSomeRequiredHttpPayloadWithDefault`
- `alloy#simpleRestJson` `Response` `AddMenuItemResult`
- `alloy#simpleRestJson` `Response` `CustomCodeOutput`
- `alloy#simpleRestJson` `Response` `GetEnumOutput`
- `alloy#simpleRestJson` `Response` `GetIntEnumOutput`
- `alloy#simpleRestJson` `Response` `GetMenuResponse`
- `alloy#simpleRestJson` `Response` `NotFoundError`
- `alloy#simpleRestJson` `Response` `PriceErrorTest`
- `alloy#simpleRestJson` `Response` `RoundTripDataResponse`
- `alloy#simpleRestJson` `Response` `SimpleRestJsonNoneHttpPayloadWithDefault`
- `alloy#simpleRestJson` `Response` `SimpleRestJsonNoneRequiredHttpPayloadWithDefault`
- `alloy#simpleRestJson` `Response` `SimpleRestJsonSomeHttpPayloadWithDefault`
- `alloy#simpleRestJson` `Response` `SimpleRestJsonSomeRequiredHttpPayloadWithDefault`
- `alloy#simpleRestJson` `Response` `VersionOutput`
- `alloy#simpleRestJson` `Response` `headerEndpointResponse`
- `aws.protocols#restJson1` `Request` `HttpQueryParamsOnlyRequest`
- `aws.protocols#restJson1` `Request` `RestJsonConstantQueryString`
- `aws.protocols#restJson1` `Request` `RestJsonEmptyInputAndEmptyOutput`
- `aws.protocols#restJson1` `Request` `RestJsonHttpPayloadWithStructure`
- `aws.protocols#restJson1` `Request` `RestJsonHttpPrefixHeadersArePresent`
- `aws.protocols#restJson1` `Response` `RestJsonHttpPayloadWithStructure`
- `aws.protocols#restJson1` `Response` `RestJsonHttpPrefixHeadersArePresent`
- `aws.protocols#restJson1` `Response` `RestJsonHttpResponseCode`
- `aws.protocols#restJson1` `Response` `RestJsonHttpResponseCodeWithNoPayload`

## Skipped Cases By Reason

### restJson1 server generation and malformed request rejection are not implemented.

- Count: 191
- `aws.protocols#restJson1` `MalformedRequest` *(191 cases — see source for full list)*

### Official request/response conformance execution is not yet enabled for this case.

- Count: 221
- `alloy#simpleRestJson` `Request` `PrimitivesEncodingRequest`
- `alloy#simpleRestJson` `Response` `PrimitivesEncodingResponse`
- `aws.protocols#restJson1` `Request` *(110 cases)*
- `aws.protocols#restJson1` `Response` *(96 cases — see source for full list)*

### Full union protocol encodings are not implemented.

- Count: 39
- `alloy#simpleRestJson` `Request` `OpenUnionsKnownDiscriminatedUnionCase`
- `alloy#simpleRestJson` `Request` `OpenUnionsKnownTaggedUnionCase`
- `alloy#simpleRestJson` `Request` `OpenUnionsUnknownDiscriminatedUnionCase`
- `alloy#simpleRestJson` `Request` `OpenUnionsUnknownTaggedUnionCase`
- `alloy#simpleRestJson` `Response` `OpenUnionsKnownDiscriminatedUnionCase`
- `alloy#simpleRestJson` `Response` `OpenUnionsKnownTaggedUnionCase`
- `alloy#simpleRestJson` `Response` `OpenUnionsUnknownDiscriminatedUnionCase`
- `alloy#simpleRestJson` `Response` `OpenUnionsUnknownTaggedUnionCase`
- `aws.protocols#restJson1` *(31 union cases — see source for full list)*

### AWS service-specific restJson1 protocol fixtures are outside the current generated-client slice.

- Count: 8
- `aws.protocols#restJson1` `Request` `AcceptHeaderStarRequestTest`
- `aws.protocols#restJson1` `Request` `AcceptHeaderStarStarRequestTest`
- `aws.protocols#restJson1` `Request` `ApiGatewayAccept`
- `aws.protocols#restJson1` `Request` `GlacierAccountId`
- `aws.protocols#restJson1` `Request` `GlacierChecksums`
- `aws.protocols#restJson1` `Request` `GlacierMultipartChecksums`
- `aws.protocols#restJson1` `Request` `GlacierVersionHeader`
- `aws.protocols#restJson1` `Request` `RestJsonRecursiveStructuresValidate`

### Other skip reasons

- **Endpoint and host-prefix binding traits are not implemented** (3 cases)
- **`alloy#preserveKeyOrder` behavior is not implemented** (2 cases)
- **Greedy label URI expansion is not implemented** (1 case)
- **HTTP checksum traits are not implemented** (1 case)
