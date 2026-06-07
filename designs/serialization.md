# Serialization

How NSmithy serializes and deserializes Smithy shapes at runtime.

## Goals

- Generated model types are plain C# data types. They do not know about
  JSON, XML, CBOR, HTTP bindings, or serializer visitors.
- Schemas are the runtime source of truth for Smithy shape metadata, member
  access, builders, and traits.
- Codecs are compiled once from a schema and produce boxing-free typed
  readers and writers on the hot path.
- Protocol implementations compose schemas and codecs instead of generating
  protocol-specific serialization code into every shape.

## Schema

`FunctionalSchema<T>` (in `NSmithy.Core.Functional`) is a runtime description
of a Smithy shape. The generator emits one immutable schema graph per service
closure. The generated POCO type and the schema live separately:

```csharp
public sealed record GetWidgetInput(string Id, string? Filter);

public static partial class GetWidgetInputSchema
{
    public static FunctionalSchema<GetWidgetInput> FunctionalSchema { get; } = ...;
}
```

A schema carries:

- `ShapeId Id` — the fully qualified Smithy shape identifier.
- `ShapeKind Kind` — the shape kind (`Structure`, `Union`, `List`, `Map`,
  `String`, etc.).
- `IReadOnlyDictionary<ShapeId, Trait> Traits` — traits applied to the shape.
- For structures: typed member accessors (getter, setter), builder factory and
  finalizer, required/default metadata, and member traits.
- For lists and maps: element/value schemas and typed builder operations.
- For unions: case matcher, case getter, and case constructor per variant.
- Lazy schema references for recursive shape graphs.

The typed `FunctionalSchema<T>` is used when the C# type is statically known.
The erased `FunctionalSchema` base is used by protocol code that walks an
operation generically.

Traits stay on schemas and members. Core schemas carry any Smithy trait, but
protocol packages decide which traits they interpret. REST protocols interpret
`@httpLabel`, `@httpHeader`, `@httpPayload`, and `@timestampFormat`; a gRPC
protocol can ignore those bindings entirely.

### Structure schemas and typed builders

Structure schemas expose two visitor interfaces — one for serialization
(container → values) and one for deserialization (values → builder):

```csharp
public interface IFunctionalStructSchema<T, TBuilder> : IFunctionalStructSchema<T>
{
    TBuilder CreateTypedBuilder();
    T Build(TBuilder builder);
    void VisitMembers(IFunctionalMemberVisitor<T, TBuilder> visitor);
}

public interface IFunctionalMemberVisitor<TContainer, TBuilder>
{
    void Visit<TValue>(IFunctionalMemberSchema<TContainer, TBuilder, TValue> member);
}
```

`IFunctionalMemberSchema<TContainer, TBuilder, TValue>` exposes both
`GetValue(TContainer)` (for reads) and `SetValue(TBuilder, TValue)` (for
writes). Codecs use these directly without boxing.

Collection schemas follow the same pattern:

```csharp
public interface IFunctionalListSchema<TCollection, TElement, TBuilder>
    : IFunctionalListSchema<TCollection, TElement>
{
    TBuilder CreateTypedBuilder();
    void Add(TBuilder builder, TElement value);
    TCollection Build(TBuilder builder);
}
```

### Lazy schemas

Recursive shape graphs use lazy references to break cycles:

```csharp
var nodeSchema = FunctionalSchemas.Lazy<Node>(() => builtNodeSchema);
```

`FunctionalLazySchema<T>` is a thin transparent wrapper. It overrides `Id`,
`Kind`, `Traits`, and `Resolved` to delegate to the target. All dispatch code
calls `.Resolved` before performing `is` checks or casts, so a lazy wrapper is
invisible to codecs and protocol code.

### Projections

A `FunctionalStructProjection<T>` keeps the same container type but narrows
the visible member set. REST protocols use projections to separate body members
from label, header, and query members:

```csharp
var bodyProjection = FunctionalSchemas.Project(inputSchema, bodyMembers);
bodyFormat.Serialize(bodyProjection, input);
```

## Codec Design

A codec is compiled once from a schema. The compiled result is a tree of typed
reader and writer objects that mirror the schema graph. On the hot path, no
schema dispatch or trait lookup occurs — all decisions are baked into the
compiled reader/writer tree.

### Interfaces

```csharp
public interface IFunctionalCodec<TValue, TPayload>
{
    TPayload Serialize(TValue value);
    TValue Deserialize(TPayload payload);
}

public interface IFunctionalProjectionCodec<TValue, TPayload>
{
    TPayload Serialize(TValue value);
    void ReadInto(TPayload payload, object builder);
}
```

JSON codecs:

```csharp
var personCodec = FunctionalJsonCodec.FromSchema(PersonSchema.FunctionalSchema);
var json = personCodec.Serialize(person);
var roundTrip = personCodec.Deserialize(json);
```

### Compilation

`FunctionalJsonCodec.FromSchema<T>` walks the schema graph once and produces
an `IJsonValueReader<T>` / `IJsonValueWriter<T>` tree. Each node in the tree
is a small sealed class with the exact concrete types it needs captured in its
generic parameters.

For a structure member typed `int`:

```
JsonMemberReader<TContainer, TBuilder, int>
  .ReadInto(TBuilder builder, JsonElement element)
      value = IntegerJsonValueReader.Read(element)   // element.GetInt32() → int, no box
      member.SetValue(builder, value)                // Action<TBuilder, int>, no box
```

For a list typed `IReadOnlyList<string>`:

```
ListJsonValueReader<IReadOnlyList<string>, string, List<string>>
  .Read(JsonElement element)
      builder = schema.CreateTypedBuilder()          // new List<string>(), typed
      for each element:
          schema.Add(builder, reader.Read(element))  // string, no box
      return schema.Build(builder)                   // typed
```

The entire deserialization path for value types is boxing-free.

### Caching

`FunctionalJsonCodec.FromSchema` is stateless and can be cached. The
`RestOperationBinding` (see Protocol Binding below) caches body projections and
protocol-level precomputations. For generated clients, the binding is computed
once at first use and stored in a `ConditionalWeakTable` keyed on the operation
schema.

## Codec Responsibilities

Codecs serialize Smithy data shapes into a wire payload for a particular body
format:

- `NSmithy.Codecs.Json` — JSON documents
- `NSmithy.Codecs.Xml` — XML documents
- `NSmithy.Codecs.Cbor` — CBOR documents

Codecs read schema metadata at compilation time. They handle body-format traits
such as XML names, timestamp formats, enum values, sparse collections, and
document nodes. They do not build HTTP requests, expand URI labels, choose
headers, or map status codes — those are protocol responsibilities.

## Protocol Binding

Protocols bind operation schemas to transports. For HTTP REST-style protocols,
`NSmithy.Protocols.Rest` provides a shared binding layer.

### RestOperationBinding

A `RestOperationBinding<TInput, TOutput>` precomputes everything that can be
determined from the operation schema before any request arrives:

- HTTP method (resolved to `System.Net.Http.HttpMethod` singleton)
- URI template string
- Label, header, query, queryParams, and payload member lists (partitioned
  in a single pass at construction time)
- Precomputed `FunctionalStructProjection` for body members
- `HashSet<string>` of bound query parameter names (for `@httpQueryParams`
  exclusion)

`RestOperationBinding.From(operation)` computes and caches the binding keyed on
the operation schema instance (via `ConditionalWeakTable`). On every subsequent
request, `SerializeRequest` and `DeserializeRequest` iterate precomputed lists
directly — no trait lookups, no LINQ, no allocations for the schema-analysis
portion.

### Protocol layering

```
FunctionalRestJsonProtocol.SerializeRequest(operation, input)
  → RestOperationBinding.From(operation)          // cached lookup
  → FunctionalRestProtocol.SerializeRequest(binding, input, BodyFormat)
      BuildRequestUri(binding.UriTemplate, binding.LabelMembers, input)
      AppendQuery / AppendQueryParams              // precomputed lists
      new SmithyHttpRequest(binding.HttpMethod, uri)
      AddHeader / AddPrefixedHeaders               // precomputed lists
      bodyFormat.Serialize(binding.InputBodyProjection, input)
```

The body format (`IFunctionalRestBodyFormat`) delegates to the JSON codec:

```csharp
public byte[] Serialize<T>(FunctionalStructProjection<T> projection, T value)
{
    var codec = FunctionalJsonCodec.FromProjection(projection);
    return Encoding.UTF8.GetBytes(codec.Serialize(value));
}
```

This keeps the layering explicit:

1. **Schema** — describes Smithy shapes, members, traits, and construction.
2. **Codec** — compiles a boxing-free typed reader/writer tree from a schema.
3. **Protocol** — precomputes per-operation HTTP binding partitions; on each
   request, iterates precomputed member lists and delegates body to the codec.
4. **Transport** — sends and receives bytes, headers, method, URI, and status.

## HTTP Value Codecs

HTTP labels, query parameters, and headers are not JSON values. REST protocols
format them with Smithy HTTP binding rules:

- Strings and enums are written as their string values.
- Numbers and booleans use Smithy string representations.
- Floats and doubles support `NaN`, `Infinity`, and `-Infinity`.
- Timestamps use the member's timestamp format, defaulting to `http-date` for
  headers and `date-time` elsewhere.
- `@mediaType` strings are base64-encoded for HTTP binding values.
- Header lists are comma-separated with RFC 7230 quoting and escaping rules.

These conversions go through `ParseHttpValue` / `FormatHttpValue` in
`FunctionalRestProtocol`, which operate on `string` and return `object?`. HTTP
binding deserialization therefore uses the erased `SetObject` path and boxes
member values. This is unavoidable without deeper changes to the HTTP binding
layer, and it affects only label/header/query members (typically a handful per
operation), not body deserialization.

## Generated Code Shape

For each shape, the generator emits:

1. The plain C# type (record, class, or enum).
2. A generated builder type for structures (used during deserialization).
3. A static schema (`FunctionalSchema<T>`) with typed member accessors,
   builder factory, and builder finalizer.

For each operation, the generator emits a `FunctionalOperationSchema<TInput, TOutput>`
that references the input and output schemas plus operation traits. Generated
clients pass operation schemas to `FunctionalRestJsonProtocol`, which handles
caching and protocol dispatch.

Model types do not implement serialization interfaces and do not contain
serializer callbacks. All wire-format behavior lives in the schema, codec, and
protocol layers.

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

Core member metadata could store a normalized location such as `Body`,
`Header`, `Query`, or `Label`.

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
