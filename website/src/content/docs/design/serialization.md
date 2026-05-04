---
title: Serialization
description: How NSmithy serializes and deserializes Smithy shapes at runtime.
---

How NSmithy serializes and deserializes Smithy shapes at runtime.

## Goals

- Protocol implementations must be **shared**: one codec for all clients and
  servers using the same wire format, not one per service.
- Codecs must be **swappable** at runtime: a consumer can provide a different
  JSON implementation without regenerating code.
- Serialization logic must be **allocation-friendly** on the hot path: avoid
  boxing, reflection, and closures in the common case.
- Codecs must be **schema-aware**: they consult Smithy traits (`@xmlName`,
  `@httpHeader`, `@timestampFormat`, etc.) at the correct layer rather than
  embedding protocol logic in generated code.

## Schema

`Schema` (in `NSmithy.Core`) is a runtime description of a Smithy shape. It is
generated once per shape (as a `static readonly` field on the generated type)
and shared across all serialize/deserialize calls.

A schema carries:

- `ShapeId Id` — the fully qualified Smithy shape identifier.
- `ShapeKind Kind` — the shape kind (Structure, Union, List, Map, String, etc.).
- `IReadOnlyDictionary<ShapeId, Trait> Traits` — traits applied to the shape,
  keyed by trait id.
- `IReadOnlyList<Schema> Members` — member schemas in declaration order
  (structures and unions only).
- `Schema? Target` — the target shape for a member schema.
- `Schema? ListMember`, `Schema? MapKey`, `Schema? MapValue` — element/key/value
  schemas for collection shapes.

Codecs read traits from schemas rather than from generated code, keeping
protocol-specific binding decisions out of the generated layer.

## Codec Interface

`ISmithyCodec` (in `NSmithy.Core.Serde`) is the top-level factory:

```csharp
public interface ISmithyCodec
{
    string MediaType { get; }
    IShapeSerializer CreateSerializer(Stream sink);
    IShapeDeserializer CreateDeserializer(ReadOnlySequence<byte> source);
}
```

Implementations: `NSmithy.Codecs.Json`, `NSmithy.Codecs.Xml`, `NSmithy.Codecs.Cbor`.

## Serializer

`IShapeSerializer` is a visitor that writes one shape value to the codec's
output:

```csharp
public interface IShapeSerializer : IDisposable
{
    void WriteStruct(Schema schema, ISerializableStruct value);
    void WriteList<TState>(Schema schema, TState state, int size,
        Action<TState, IShapeSerializer> consumer);
    void WriteMap<TState>(Schema schema, TState state, int size,
        Action<TState, IMapSerializer> consumer);
    void WriteBoolean(Schema schema, bool value);
    void WriteString(Schema schema, string value);
    // ... one method per Smithy scalar type ...
    void WriteNull(Schema schema);
    void Flush();
}
```

`WriteList` and `WriteMap` use a generic state parameter to pass loop state
without allocating a closure.

## Deserializer

`IShapeDeserializer` is the mirror: a visitor that reads one shape value from
the codec's source. Generated shapes call the appropriate `Read*` method for
each member.

## Generated Shape Contracts

Every generated structure implements two interfaces:

```csharp
public interface ISerializableShape
{
    void Serialize(IShapeSerializer serializer);
}

public interface IDeserializableShape
{
    static abstract TSelf Deserialize(IShapeDeserializer deserializer);
}
```

A generated structure calls `WriteStruct`, which calls back into the structure's
`ISerializableStruct` implementation to write each member. The struct
implementation calls the appropriate `Write*` method per member, passing the
member's `Schema` so the codec can apply traits.

## Separation of Concerns

The serialization design keeps three concerns separate:

1. **Shape data** — the generated C# type (`record`, `class`, etc.).
2. **Shape metadata** — the `Schema` field on the type.
3. **Wire format** — the codec implementation (`NSmithy.Codecs.*`).

Generated code only calls the codec through the `IShapeSerializer` /
`IShapeDeserializer` interfaces. It never accesses JSON, XML, or CBOR APIs
directly. This means the wire format can be swapped at runtime by providing a
different `ISmithyCodec` without regenerating or recompiling the service model
types.

## Protocol Binding

`ISmithyCodec` handles the body. Protocols like `alloy#simpleRestJson` need to
bind additional Smithy traits to the HTTP layer:

- `@httpHeader` / `@httpPrefixHeaders` — member goes into a request/response
  header, not the body.
- `@httpLabel` — member is bound to a URI template segment.
- `@httpQuery` / `@httpQueryParams` — member is bound to a query parameter.
- `@httpPayload` — member is the entire HTTP body.
- `@httpResponseCode` — member receives the HTTP status code.

Protocol implementations in `NSmithy.Protocols.*` compose `IShapeSerializer`
and `IShapeDeserializer` instances for the body with additional binding for
these HTTP-level traits. An `InterceptingSerializer` pattern dispatches member
writes to different sinks (body writer vs. header writer) based on the member's
schema traits.

## Alternatives Considered

### Reflection-based serialization

Scanning properties at runtime via reflection is common in .NET serializers
(e.g. `System.Text.Json` without source generation).

**Rejected because:**

- Reflection loses the Smithy schema: there is no general way to recover
  `@xmlName`, `@httpHeader`, or `@timestampFormat` from a C# property attribute
  without coupling the generated code to a specific serializer.
- Reflection is slower and produces more allocations than a direct visitor call.
- Source-generated serialization (like `System.Text.Json` source generation)
  requires annotating generated types with serializer-specific attributes, which
  couples the model layer to one codec.

### Source-generated JSON via `System.Text.Json`

Using `[JsonSerializable]` attributes and source generation would reduce
runtime overhead and integrate with the .NET JSON ecosystem.

**Rejected (for now) because:**

- `System.Text.Json` does not natively understand Smithy traits. Custom
  converters would be required for every trait that affects wire representation,
  recreating the schema-awareness problem.
- Switching protocols (e.g. from JSON to CBOR) would require different
  attributes or converters on the same generated types.
- The visitor pattern keeps the generated shape types protocol-neutral.

## Related Docs

- [Shape Mapping](./shapes/) — C# type mapping
- [HTTP Interfaces](./http-interfaces/) — HTTP transport
- [Codegen Architecture](./codegen-architecture/) — codegen pipeline
