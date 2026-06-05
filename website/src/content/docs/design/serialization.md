---
title: Serialization
description: How NSmithy serializes and deserializes Smithy shapes at runtime.
---

## Goals

- Generated model types should be plain C# data types. They do not know about
  JSON, XML, CBOR, HTTP bindings, or serializer visitors.
- Schemas are the runtime source of truth for Smithy shape metadata, member
  access, builders, and traits.
- Codecs are schema-bound values with simple `Serialize` and `Deserialize`
  methods.
- Protocol implementations compose schemas and codecs instead of generating
  protocol-specific serialization code into every shape.
- Runtime behavior should be easy to reason about first, while still leaving a
  direct path to precomputed metadata and generated delegates for performance.

## Schema

`Schema` (in `NSmithy.Core`) is a runtime description of a Smithy shape. The
generator emits one immutable schema graph per service closure. The generated
POCO type and the schema live separately:

```csharp
public sealed record GetWidgetInput(string Id, string? Filter);

public static class GetWidgetInputSchemas
{
    public static readonly Schema<GetWidgetInput> Schema = ...;
}
```

A schema carries:

- `ShapeId Id` — the fully qualified Smithy shape identifier.
- `ShapeKind Kind` — the shape kind (`Structure`, `Union`, `List`, `Map`,
  `String`, etc.).
- `IReadOnlyDictionary<ShapeId, Trait> Traits` — traits applied to the shape.
- Member metadata for structures and unions, including Smithy member name,
  target schema, required/default metadata, member traits, and typed accessors.
- Element/key/value schemas for lists and maps.
- Builder metadata for aggregate shapes that need staged construction during
  deserialization.
- Lazy schema references for recursive shape graphs.

The typed schema is used when generated or handwritten code has the static C#
type available. The erased `Schema` base is used by protocol code that walks an
operation generically.

Traits stay on schemas and members. Core schemas can carry any Smithy trait, but
protocol packages decide which traits they interpret. For example, REST
protocols interpret `@httpLabel`, `@httpHeader`, `@httpPayload`, and
`@timestampFormat`; a gRPC protocol can ignore those REST bindings and serialize
the input into its binary body representation.

Structure schemas expose ordered traversal, name lookup, and a typed member
visitor:

```csharp
public interface IStructSchema<T> : IStructSchema
{
    void VisitMembers(IMemberVisitor<T> visitor);
}

public interface IMemberVisitor<TContainer>
{
    void Visit<TValue>(IMemberSchema<TContainer, TValue> member);
}
```

The ordered member list and `GetMember(name)` support generic protocol and
deserialization code. The visitor preserves member value types during typed
serialization, so codecs can read `MemberSchema<Person, int>` without boxing the
member through `object`.

Recursive schemas use lazy references:

```csharp
var nodeListSchema = Schemas.List(
    ShapeId.Parse("example#NodeList"),
    Schemas.Lazy(() => NodeSchemas.Schema)
);
```

Top-level structure projections are first-class projection values. A projection
keeps the same container type but narrows the visible member set:

```csharp
var bodyProjection = Schemas.Project(
    UpdateInputSchemas.Schema,
    [UpdateInputSchemas.Name, UpdateInputSchemas.Age]
);
```

Projection codecs can serialize a projection with the original value:

```csharp
JsonCodec.FromProjection(bodyProjection).Serialize(input);
```

Projection codecs do not expose `Deserialize(payload) -> T`, because a
projection often carries only part of `T`. They expose a merge operation instead:

```csharp
JsonCodec.FromProjection(bodyProjection).ReadInto(payload, builder);
```

This is how REST protocols keep body serialization typed and body
deserialization explicit when labels, headers, or query parameters are excluded
from the document body.

## Codec Interface

A codec is bound to a schema. After construction, callers only pass values and
payloads:

```csharp
public interface ICodec<TValue, TPayload>
{
    TPayload Serialize(TValue value);
    TValue Deserialize(TPayload payload);
}

public interface ICodecFactory<TPayload>
{
    ICodec<TValue, TPayload> FromSchema<TValue>(Schema<TValue> schema);
}

public interface IProjectionCodec<TValue, TPayload>
{
    TPayload Serialize(TValue value);
    void ReadInto(TPayload payload, object builder);
}
```

JSON codecs expose the same shape-specific API:

```csharp
var personCodec = JsonCodec.FromSchema(PersonSchemas.Schema);
var json = personCodec.Serialize(person);
var roundTrip = personCodec.Deserialize(json);
```

Codec factories preserve the schema value type. Protocols that select members
from a structure use typed member dispatch and typed structure projections
rather than asking a body codec factory to compile an erased schema. Full
schemas use `FromSchema`; projections use a separate `FromProjection` operation
and a projection codec.

## Codec Responsibilities

Codecs serialize Smithy data shapes into a wire payload for a particular body
format:

- `NSmithy.Codecs.Json` handles JSON documents.
- `NSmithy.Codecs.Xml` handles XML documents.
- `NSmithy.Codecs.Cbor` handles CBOR documents.

Codecs read schema metadata directly. They know about body-format traits such
as XML names, timestamp formats, enum values, sparse collections, and document
nodes. They do not know how to build an HTTP request, expand URI labels, choose
headers, or map status codes; those are protocol responsibilities.

## Protocol Binding

Protocols bind operation schemas to transports. For HTTP REST-style protocols,
`NSmithy.Protocols.Rest` provides the shared projection:

- `@http` selects method, URI template, and success status code.
- `@httpLabel` expands labels in the URI path, including greedy labels.
- `@httpQuery` and `@httpQueryParams` write or read query string values.
- `@httpHeader` and `@httpPrefixHeaders` write or read headers.
- `@httpPayload` binds a member to the entire HTTP body.
- `@httpResponseCode` binds a member to the response status code.
- Unbound members are projected into a typed body projection and passed to the
  body format.

REST JSON and REST XML do not duplicate this binding logic. They supply body
codec factories to the shared REST protocol projection:

```csharp
var request = RestProtocol.SerializeRequest(
    operationSchema,
    input,
    JsonCodec.Factory,
    endpoint
);
```

This keeps the layering explicit:

1. **Schema** describes Smithy shapes, members, traits, and construction.
2. **Codec** converts Smithy data shapes to and from a body payload.
3. **Protocol** projects operation input/output between schemas, codecs, and
   transport-specific fields.
4. **Transport** sends and receives bytes, headers, method, URI, and status.

## HTTP Value Codecs

HTTP labels, query parameters, and headers are not JSON values. REST protocols
format them with Smithy HTTP binding rules:

- strings and enums are written as their string values.
- numbers and booleans use Smithy string representations.
- floats and doubles support `NaN`, `Infinity`, and `-Infinity`.
- timestamps use the binding's timestamp format, defaulting to `http-date` for
  headers.
- `@mediaType` strings are base64 encoded for HTTP binding values.
- header lists are comma-separated with string quoting and escaping rules.

These conversions are narrow HTTP binding codecs, separate from JSON or XML
body codecs.

## Generated Code Shape

For each shape, the generator emits:

1. The plain C# type.
2. A builder type when deserialization needs staged construction.
3. A static schema description with typed accessors, builder hooks, member
   schemas, and traits.

For each operation, the generator emits an operation schema that references the
input, output, and error schemas plus operation traits. Generated clients and
servers pass operation schemas to protocol implementations.

Model types do not implement serialization interfaces and do not contain
serializer callbacks. This keeps generated POCOs usable as ordinary domain
objects and keeps all wire-format behavior in the schema, codec, and protocol
layers.

## Performance Direction

The schema graph is immutable and generated once. That gives the runtime a
natural place to precompute:

- member arrays in declaration order.
- name-to-member lookup tables for deserialization.
- typed getter and builder setter delegates.
- typed member visitor dispatch.
- top-level structure projections.
- protocol projections for body/header/query/label partitions.
- schema-bound codec instances.

The initial design can prioritize clarity, but the model does not require
reflection or per-call shape analysis. Hot paths can cache schema-bound codecs
and protocol projections without changing the public API.

## Alternatives Considered

### Reflection-based serialization

Scanning properties at runtime via reflection is common in .NET serializers.

**Rejected because:**

- Reflection loses Smithy member metadata unless generated POCOs are annotated
  with serializer-specific attributes.
- Multiple protocols would need competing attributes or converters on the same
  model type.
- Reflection makes protocol projections harder to precompute and reason about.

### Per-shape serializer methods

Generated shapes could contain explicit methods that call a serializer visitor.

**Rejected because:**

- It couples the POCO to serialization mechanics.
- Protocol projections still need to intercept or redirect member writes.
- Deserialization becomes callback-heavy and less direct than constructing from
  schema builder metadata.

### Protocol locations in core schema metadata

Core member metadata could store a normalized location such as `Body`,
`Header`, `Query`, or `Label`.

**Rejected because:**

- A single Smithy model can be used by multiple protocols.
- Location is a protocol interpretation of traits, not an intrinsic property of
  the shape.
- Shared traits should remain available to protocols without forcing every
  protocol to agree on one projection.
