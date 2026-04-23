# Supported Surface

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

Generated files include Smithy metadata attributes from `SmithyNet.Core`.

## Nullability

Generated C# nullability is authoritative.

Required reference members are emitted as non-nullable and enforced through
generated constructors. Optional members remain nullable unless a Smithy default
applies.

Runtime request binding and deserialization still validate external input, since
remote callers can omit required data even when generated .NET types are strict.

## JSON

`SmithyNet.Json` supports JSON serialization and deserialization for generated
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

`SmithyNet.Http` provides:

- `SmithyHttpRequest`
- `SmithyHttpResponse`
- `IHttpTransport`
- `HttpClientTransport`

`SmithyNet.Client` provides:

- `SmithyOperationInvoker`
- client middleware
- basic retry middleware
- HTTP error dispatch through generated error deserializers
- `SmithyClientOptions`

## Server Runtime

`SmithyNet.Server` provides the first server-side runtime primitives:

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

The current end-to-end example is:

- `examples/polyglot/java`: Java `restJson1` server
- `examples/polyglot/scala`: Scala `simpleRestJson` server
- `examples/polyglot/dotnet`: generated .NET clients using local Smithy.NET
  packages
- `examples/rpcv2cbor/dotnet`: generated .NET `rpcv2Cbor` client with an
  in-process mock peer

Run the Java and Scala services first, then run the .NET client:

```bash
cd examples/polyglot/java
gradle :server:run
```

```bash
cd examples/polyglot/scala
sbt run
```

```bash
cd examples/polyglot/dotnet
dotnet run -- world http://localhost:8082 http://localhost:8081
```

```bash
cd examples/rpcv2cbor/dotnet/client
dotnet run -- world
```
