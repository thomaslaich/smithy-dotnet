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

Generated files include Smithy metadata attributes from `SmithyNet.Core`.

## Nullability

The default generated input model is non-authoritative for nullable reference
types. Required input members can still be nullable because remote callers may
omit values and because client-side construction is not yet a validation layer.

The generator also has an authoritative nullability mode used by tests, but the
MSBuild integration currently uses the default mode.

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

## Protocols

The implemented protocol slice is `aws.protocols#restJson1` for generated
clients.

The next protocol target is `alloy#simpleRestJson`. The intended split is:

- generated clients support both `aws.protocols#restJson1` and
  `alloy#simpleRestJson`
- generated servers target `alloy#simpleRestJson` first
- `restJson1` server generation is not planned for the initial server runtime

Current generated request bindings include:

- `@http`
- `@httpLabel`
- `@httpQuery`
- `@httpHeader`
- `@httpPayload`
- default JSON body serialization for unbound input members

Current generated response bindings include:

- JSON body deserialization
- `@httpHeader`
- `@httpPayload`
- `@httpResponseCode`
- generated Smithy error dispatch using `@error` and `@httpError`

Protocol support is intentionally narrow until more Smithy protocol compliance
cases are covered.

## Examples

The current end-to-end example is:

- `examples/polyglot/java`: Java `restJson1` server
- `examples/polyglot/dotnet/client`: generated .NET client using local
  Smithy.NET packages

Run the Java service first, then run the .NET client:

```bash
cd examples/polyglot/java
gradle :server:run
```

```bash
cd examples/polyglot/dotnet/client
dotnet run -- http://localhost:8082 world
```
