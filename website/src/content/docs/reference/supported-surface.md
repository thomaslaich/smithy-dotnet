---
title: Supported Surface
description: What the current NSmithy preview generates and supports.
---

This document describes the current preview implementation, not the final
project goal.

## Shape Generation

The C# generator emits files for:

- structures
- lists
- sets
- maps with string keys
- string enums
- int enums
- unions
- Smithy error structures
- `restJson1` services as typed clients
- `simpleRestJson` services as typed clients
- `simpleRestJson` services as typed ASP.NET Core server surfaces
- `rpcv2Cbor` services as typed clients (early preview)

Generated files include Smithy metadata attributes from `NSmithy.Core`.

## Nullability

Generated C# nullability is authoritative.

Required reference members are emitted as non-nullable and enforced through
generated constructors. Optional members remain nullable unless a Smithy default
applies.

Runtime request binding and deserialization still validate external input, since
remote callers can omit required data even when generated .NET types are strict.

## JSON

`NSmithy.Codecs.Json` supports JSON serialization and deserialization for generated
Smithy shapes using generated metadata attributes.

Covered shape kinds:

- structures
- lists and sets
- maps
- string enums
- int enums
- unions
- `document`
- blobs as base64
- timestamps as `DateTimeOffset`

The current implementation is reflection-based. Roslyn/source-generated
serializer metadata is planned but not implemented.

## HTTP Client Runtime

`NSmithy.Http` provides:

- `SmithyHttpRequest`
- `SmithyHttpResponse`
- `IHttpTransport`
- `HttpClientTransport`

`NSmithy.Client` provides:

- `SmithyOperationInvoker`
- client middleware
- basic retry middleware
- HTTP error dispatch through generated error deserializers
- `SmithyClientOptions`

## Server Runtime

`NSmithy.Server` provides the first server-side runtime primitives:

- service and operation descriptors
- generated operation handler interfaces and aggregate service handler interfaces
- generated DI helpers
- generated ASP.NET Core endpoint mapping extensions

The generated ASP.NET Core mapping currently covers the first HTTP skeleton:

- route registration from Smithy `@http`
- handler resolution through ASP.NET Core dependency injection
- `@httpLabel`, `@httpQuery`, and `@httpHeader` request binding
- JSON request body and member binding for simple payloads
- JSON output serialization, including first-pass `@httpHeader`, `@httpPayload`,
  and `@httpResponseCode` response bindings
- modeled error serialization with `@httpError`

Response binding edge cases still need broader protocol tests.

## Protocols

The implemented protocol slices are:

- generated clients for `aws.protocols#restJson1`
- generated clients for `alloy#simpleRestJson`
- generated ASP.NET Core servers for `alloy#simpleRestJson`
- generated clients for `smithy.protocols#rpcv2Cbor` (early preview)

Generated `restJson1` servers are not part of this preview.

Current generated request bindings include:

- `@http`
- `@httpLabel`
- `@httpQuery`
- `@httpQueryParams` for map-shaped dynamic query parameters
- `@httpHeader`
- `@httpPrefixHeaders` for map-shaped dynamic headers
- `@httpPayload`
- default JSON body serialization for unbound input members

Current generated response bindings include:

- JSON body deserialization
- `@httpHeader`
- `@httpPrefixHeaders` for map-shaped dynamic headers
- `@httpPayload`
- `@httpResponseCode`
- generated Smithy error dispatch using `@error` and `@httpError`

Protocol support is intentionally narrow until more Smithy protocol compliance
cases are covered. Dynamic query parameter and prefix-header support currently
targets string-keyed, string-valued generated map shapes.

## Examples

The current end-to-end examples are:

- `examples/simple-rest-json/dotnet`: generated NSmithy client and ASP.NET Core server using `alloy#simpleRestJson`
- `examples/rpcv2cbor/dotnet`: generated `rpcv2Cbor` client with an in-process mock transport
- `examples/grpc/dotnet`: generated HTTP and gRPC client/server from one `alloy.proto#grpc` model
- `examples/polyglot/dotnet`: generated .NET clients calling Java and Scala servers
