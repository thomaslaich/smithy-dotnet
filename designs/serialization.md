# Serialization

How NSmithy serializes and deserializes Smithy shapes at runtime.

Serialization has four layers:

1. **Model types** are plain C# values.
2. **Schemas** describe Smithy metadata, typed member access, and construction.
3. **Codecs** compile schemas into body readers and writers.
4. **Protocols** project operation schemas into transport requests and responses.

## Goals

- Generated model types are plain C# data types. They do not know about JSON,
  XML, CBOR, HTTP bindings, or serializer visitors.
- Schemas are the runtime source of truth for Smithy shape metadata, member
  access, builders, and traits.
- Codecs are compiled once from a schema and produce boxing-free typed readers
  and writers on the hot path.
- Protocol implementations compose schemas and codecs instead of generating
  protocol-specific serialization code into every shape.

## Runtime Model

The generator emits plain model types and separate schema definitions:

```csharp
public sealed record GetWidgetInput(string Id, string? Filter);

public static class GetWidgetInputSchema
{
    public static Schema<GetWidgetInput> Schema { get; } = ...;
}
```

For each shape, the generator emits:

1. The plain C# type (record, class, enum, list alias, map alias, etc.).
2. A generated builder type when deserialization needs staged construction.
3. A static `Schema<T>` with typed member accessors, builder factory, builder
   finalizer, shape traits, and member traits.

For each operation, the generator emits an `OperationSchema<TInput, TOutput>`
that references the input and output schemas plus operation traits. Generated
clients pass operation schemas to protocol adapters such as `RestJsonProtocol`.

Model types do not implement serialization interfaces and do not contain
serializer callbacks. All wire-format behavior lives in the schema, codec, and
protocol layers.

## Schema Model

`Schema<T>` (in `NSmithy.Core`) is a runtime description of a Smithy shape. The
typed form is used when the C# type is statically known. The erased `Schema`
base is used only at boundaries that genuinely need shape-generic dispatch,
such as protocol analysis over an operation graph.

A schema carries:

- `ShapeId Id` - the fully qualified Smithy shape identifier.
- `ShapeKind Kind` - the shape kind (`Structure`, `Union`, `List`, `Map`,
  `String`, etc.).
- `IReadOnlyDictionary<ShapeId, Trait> Traits` - traits applied to the shape.
- For structures: typed member accessors, builder factory and finalizer,
  required/default metadata, and member traits.
- For lists and maps: element/value schemas and typed builder operations.
- For unions: case matcher, case getter, and case constructor per variant.
- Lazy schema references for recursive shape graphs.

Traits stay on schemas and members. Core schemas carry any Smithy trait, but
protocol packages decide which traits they interpret. REST protocols interpret
`@httpLabel`, `@httpHeader`, `@httpPayload`, and `@timestampFormat`; a gRPC
protocol can ignore those bindings entirely.

### Schema Internals

Structure schemas expose typed member visitors so codecs can compile member
readers and writers without boxing:

```csharp
public interface IStructSchema<T, TBuilder> : IStructSchema<T>
{
    TBuilder CreateTypedBuilder();
    T Build(TBuilder builder);
    void VisitMembers(IMemberVisitor<T, TBuilder> visitor);
}

public interface IMemberVisitor<TContainer, TBuilder>
{
    void Visit<TValue>(IMemberSchema<TContainer, TBuilder, TValue> member);
}
```

`IMemberSchema<TContainer, TBuilder, TValue>` exposes both
`GetValue(TContainer)` for serialization and `SetValue(TBuilder, TValue)` for
deserialization.

Collection schemas follow the same pattern:

```csharp
public interface IListSchema<TCollection, TElement, TBuilder>
    : IListSchema<TCollection, TElement>
{
    TBuilder CreateTypedBuilder();
    void Add(TBuilder builder, TElement value);
    TCollection Build(TBuilder builder);
}
```

Recursive shape graphs use lazy references to break cycles:

```csharp
var nodeSchema = Schemas.Lazy<Node>(() => builtNodeSchema);
```

`LazySchema<T>` is a thin transparent wrapper. It delegates `Id`, `Kind`,
`Traits`, and `Resolved` to the target. Dispatch code calls `.Resolved` before
performing type checks, so lazy wrappers are invisible to codecs and protocol
code.

REST protocols use projections to keep the same container type while narrowing
the visible member set:

```csharp
var bodyProjection = Schemas.Project(inputSchema, bodyMembers);
JsonCodec.FromProjection(bodyProjection).Serialize(input);
```

## Codec Model

A codec is compiled once from a schema. The compiled result is a tree of typed
reader and writer objects that mirror the schema graph. On the hot path, no
schema dispatch or trait lookup occurs; those decisions are baked into the
compiled reader/writer tree.

```csharp
public interface ICodec<TValue>
{
    byte[] Serialize(TValue value);
    TValue Deserialize(ReadOnlySpan<byte> payload);
}

public interface IProjectionCodec<TValue>
{
    byte[] Serialize(TValue value);
    void ReadInto(ReadOnlySpan<byte> payload, object builder);
}
```

JSON codec usage:

```csharp
var personCodec = JsonCodec.FromSchema(PersonSchema.Schema);
var json = personCodec.Serialize(person);
var roundTrip = personCodec.Deserialize(json);
```

Codecs serialize Smithy data shapes into a wire payload for a particular body
format:

- `NSmithy.Codecs.Json` - JSON documents
- `NSmithy.Codecs.Xml` - XML documents
- `NSmithy.Codecs.Cbor` - CBOR documents

Codecs read schema metadata at compilation time. They handle body-format traits
such as XML names, timestamp formats, enum values, sparse collections, and
document nodes. They do not build HTTP requests, expand URI labels, choose
headers, or map status codes; those are protocol responsibilities.

### Codec Compilation

`JsonCodec.FromSchema<T>` walks the schema graph once and produces an
`IJsonValueReader<T>` / `IJsonValueWriter<T>` tree. Each node in the tree is a
small sealed class with the exact concrete types it needs captured in its
generic parameters.

For a structure member typed `int`:

```csharp
JsonMemberReader<TContainer, TBuilder, int>
  .ReadInto(TBuilder builder, JsonElement element)
      value = IntegerJsonValueReader.Read(element)   // element.GetInt32() -> int
      member.SetValue(builder, value)                // Action<TBuilder, int>
```

For a list typed `IReadOnlyList<string>`:

```csharp
ListJsonValueReader<IReadOnlyList<string>, string, List<string>>
  .Read(JsonElement element)
      builder = schema.CreateTypedBuilder()          // new List<string>()
      for each element:
          schema.Add(builder, reader.Read(element))  // string
      return schema.Build(builder)                   // typed collection
```

The entire body deserialization path for value types is boxing-free.

### Codec Caching

`JsonCodec.FromSchema` is stateless and can be cached. Protocol bindings cache
body projections, projection codecs, and protocol-level precomputations. For
generated clients, the binding is computed once at first use and stored in a
`ConditionalWeakTable` keyed on the operation schema.

## Protocol Model

Protocols bind operation schemas to transports. For HTTP REST-style protocols,
`NSmithy.Protocols.Rest` provides a shared binding layer that REST JSON and
REST XML can reuse.

A `RestOperationBinding<TInput, TOutput>` precomputes everything that can be
determined from the operation schema before any request arrives:

- HTTP method.
- URI template string.
- Label, header, query, queryParams, and payload member lists.
- Body projection for input/output structures.
- Projection codec for each body projection.
- Bound query parameter names for `@httpQueryParams` exclusion.

`RestOperationBinding.From(operation)` computes and caches the binding keyed on
the operation schema instance. On every subsequent request, `SerializeRequest`
and `DeserializeRequest` iterate precomputed lists directly; no trait lookup,
LINQ, or schema-analysis allocation is needed for the protocol partitioning.

Request serialization follows this shape:

```csharp
RestJsonProtocol.SerializeRequest(operation, input)
  -> RestOperationBinding.From(operation)
  -> RestProtocol.SerializeRequest(binding, input)
      BuildRequestUri(binding.UriTemplate, binding.LabelMembers, input)
      AppendQuery / AppendQueryParams
      new SmithyHttpRequest(binding.HttpMethod, uri)
      AddHeader / AddPrefixedHeaders
      binding.InputBodyCodec.Serialize(input)
```

The REST protocol does not need a separate body-format abstraction for the
common case. The protocol binding already knows which body projection belongs
to the operation and can cache the matching projection codec directly:

```csharp
public sealed class RestOperationBinding<TInput, TOutput>
{
    public StructProjection<TInput> InputBodyProjection { get; }
    public IProjectionCodec<TInput> InputBodyCodec { get; }
}
```

## HTTP Binding Values

HTTP labels, query parameters, and headers are not JSON values. REST protocols
format them with Smithy HTTP binding rules:

- Strings and enums are written as their string values.
- Numbers and booleans use Smithy string representations.
- Floats and doubles support `NaN`, `Infinity`, and `-Infinity`.
- Timestamps use the member's timestamp format, defaulting to `http-date` for
  headers and `date-time` elsewhere.
- `@mediaType` strings are base64-encoded for HTTP binding values.
- Header lists are comma-separated with RFC 7230 quoting and escaping rules.

These conversions go through typed HTTP value readers and writers in
`RestProtocol`. They are separate from JSON/XML/CBOR body codecs because HTTP
binding values are strings with Smithy-specific escaping, timestamp, list, and
media-type rules.

## Alternatives Considered

### Reflection-based serialization

Scanning properties at runtime via reflection is common in .NET serializers.

**Rejected because:**

- Reflection loses Smithy member metadata unless generated POCOs carry
  serializer-specific attributes.
- Multiple protocols would need competing attributes or converters on the same
  model type.
- Reflection prevents the precomputation that makes the hot path allocation-free.

### Per-shape serializer methods

Generated shapes could contain explicit methods that call a serializer visitor.

**Rejected because:**

- It couples the POCO to serialization mechanics.
- Protocol projections still need to intercept or redirect member writes.
- Deserialization becomes callback-heavy and harder to reason about than
  constructing via schema builder metadata.

### Protocol locations in core schema metadata

Core member metadata could store a normalized location such as `Body`, `Header`,
`Query`, or `Label`.

**Rejected because:**

- A single Smithy model can be used by multiple protocols.
- Location is a protocol interpretation of traits, not an intrinsic property of
  the shape.
- Sharing traits lets protocols interpret them independently without forcing
  agreement on one projection.

### Source-generated per-format deserializers

The Java codegen could emit typed JSON deserializers alongside each shape,
similar to `System.Text.Json` source generation.

**Rejected because:**

- It couples generated model types to a specific wire format.
- Adding a new codec (CBOR, XML, MessagePack) would require regenerating all
  models.
- The compiler pattern achieves boxing-free deserialization without
  per-format codegen, while keeping codecs swappable at runtime.
