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
that references the input and output schemas plus operation traits; for each
service it emits a `ServiceSchema` carrying the service shape id and
service-level traits. A generated client binds these into per-operation
protocols once and threads inputs and outputs through them (see
[Client Generation](#client-generation)).

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
```

The projection itself is just schema metadata: it says which members of the
container are visible for a particular protocol body. The actual codec compiled
from that projection is introduced in the codec layer below.

## Codec Model

A codec is compiled once from a schema. The compiled result is a tree of typed
reader and writer objects that mirror the schema graph. On the hot path, no
schema dispatch or trait lookup occurs; those decisions are baked into the
compiled reader/writer tree.

```csharp
public interface ICodec<TValue>
{
    byte[] Serialize(TValue value);
    TValue Deserialize(byte[] payload);
}

public interface IProjectionCodec<TValue>
{
    byte[] Serialize(TValue value);
    void ReadInto(byte[] payload, object builder);
}
```

Codec usage:

```csharp
var personCodec = JsonCodec.FromSchema(PersonSchema.Schema);
var json = personCodec.Serialize(person);
var roundTrip = personCodec.Deserialize(json);

var xmlCodec = XmlCodec.FromSchema(PersonSchema.Schema);
var xml = xmlCodec.Serialize(person);

var cborCodec = CborCodec.FromSchema(PersonSchema.Schema);
var cbor = cborCodec.Serialize(person);
```

Projection codecs are used when a protocol wants to serialize only a subset of
the members of a structure while keeping the same container type:

```csharp
var bodyProjection = Schemas.Project(inputSchema, bodyMembers);
var bodyCodec = JsonCodec.FromProjection(bodyProjection);
var body = bodyCodec.Serialize(input);
```

The same pattern exists for other body formats that support projections:

```csharp
var xmlBodyCodec = XmlCodec.FromProjection(bodyProjection);
var xmlBody = xmlBodyCodec.Serialize(input);
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

`JsonCodec.FromSchema` and `CborCodec.FromSchema` build a codec from a schema.
Rather than memoizing inside the factory, caching happens one level up: a
generated client (or server) builds each operation's protocol exactly once — a
`static readonly` field — and that operation-bound protocol holds whatever was
precomputed from the schema: the `RestOperationBinding` for REST, or the
configured codec instances for rpcv2Cbor. Because the field is initialized once,
the per-operation precomputation is paid once and reused for every call. See
[Client Generation](#client-generation).

## Protocol Model

Protocols bind operation schemas to transports. The abstraction is two
interfaces in `NSmithy.Http`:

```csharp
public interface IServiceProtocol
{
    IOperationProtocol<TInput, TOutput> ForOperation<TInput, TOutput>(
        OperationSchema<TInput, TOutput> operation);
}

public interface IOperationProtocol<TInput, TOutput>
{
    SmithyHttpRequest  SerializeRequest(TInput input);          // client
    TOutput            DeserializeResponse(SmithyHttpResponse response);
    TInput             DeserializeRequest(SmithyHttpRequest request);   // server
    SmithyHttpResponse SerializeResponse(TOutput output);

    bool    IsErrorResponse(SmithyHttpResponse response);
    string? GetErrorDiscriminator(SmithyHttpResponse response);
    TError  DeserializeError<TError>(Schema<TError> errorSchema, SmithyHttpResponse response);
    SmithyHttpResponse SerializeError<TError>(
        Schema<TError> errorSchema, TError value, string errorShapeId, int statusCode);
}
```

Each protocol provides a `ForService(ServiceSchema)` factory that returns an
`IServiceProtocol`, which in turn hands out an `IOperationProtocol` per
operation. The interface is the *unary* shape; a streaming sibling would be a
separate interface.

Every protocol-specific wire decision lives behind the implementation — codegen
no longer knows any of it:

- **Request path.** rpcv2Cbor's path is service-derived
  (`/service/{Service}/operation/{Operation}`), so its operation protocol
  computes it from the service and operation shape names. REST reads the
  `@http` trait off the operation schema and substitutes labels from the input
  per call.
- **Body codec.** rpcv2Cbor uses CBOR; restJson1 uses JSON; restXml uses XML.
- **Error discrimination.** REST reads `X-Amzn-Errortype` / `__type` / `code`;
  rpcv2Cbor reads `__type` from the CBOR body; `IsErrorResponse` decides whether
  a response is an error at all (HTTP status today, the `grpc-status` trailer for
  gRPC).

### REST binding layer

`NSmithy.Protocols.Rest` factors the REST wire format into two pieces along the
generic/non-generic seam:

- `RestOperationProtocol<TInput, TOutput>` — the per-operation `IOperationProtocol`
  implementation. It holds a `RestOperationBinding` and the `IRestBodyFormat`,
  and delegates to the stateless engine.
- `RestProtocol` — the non-generic, stateless wire engine (URI templating,
  label/query/header binding, payload, error parsing). restJson1 and restXml
  share it and differ only by `IRestBodyFormat` (JSON vs XML).

A `RestOperationBinding<TInput, TOutput>` precomputes everything determinable
from the operation schema before any request arrives — HTTP method, URI
template, the label/header/query/queryParams/payload member lists, and the
input/output body projections. `SerializeRequest`/`DeserializeRequest` then
iterate those precomputed lists directly; no trait lookup, LINQ, or
schema-analysis allocation is needed per request.

rpcv2Cbor is **not** built on `RestProtocol`. Its `IOperationProtocol`
implementation holds the request URI plus the request/response CBOR codecs
(with the per-direction default-materialization policy baked in) and writes the
CBOR envelope directly.

## Client Generation

A generated client builds the protocol objects **once**, as `static readonly`
fields, and threads inputs and outputs through them. The service protocol is
created from the service schema; each operation protocol is created from its
operation schema via `ForOperation`:

```csharp
private static readonly IServiceProtocol ServiceProtocol =
    RpcV2CborProtocol.ForService(RpcV2ProtocolSchema.Schema);

private static readonly IOperationProtocol<GreetingWithErrorsInput, GreetingWithErrorsOutput>
    GreetingWithErrorsProtocol = ServiceProtocol.ForOperation(GreetingWithErrorsSchema.Schema);
```

The per-operation method body is then protocol-agnostic — the same shape for
restJson1, restXml, simpleRestJson, and rpcv2Cbor:

```csharp
public async Task<GreetingWithErrorsOutput> GreetingWithErrorsAsync(
    GreetingWithErrorsInput input,
    CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(input);
    var request = GreetingWithErrorsProtocol.SerializeRequest(input);

    var response = await invoker
        .InvokeAsync(
            "RpcV2Protocol",
            "GreetingWithErrors",
            request,
            DeserializeGreetingWithErrorsErrorAsync,
            GreetingWithErrorsProtocol.IsErrorResponse,
            cancellationToken)
        .ConfigureAwait(false);

    return GreetingWithErrorsProtocol.DeserializeResponse(response);
}
```

### Memoization by construction

There is no codec or binding cache keyed on the schema. Memoization falls out of
calling `ForOperation` **once**: because `GreetingWithErrorsProtocol` is a
`static readonly` field, the operation protocol — and everything it precomputed
from the schema (the `RestOperationBinding` for REST, the compiled codecs for
rpcv2Cbor) — is built a single time per process and reused for every request.
The hot path does no schema analysis. This is also why
`RestOperationBinding.From` no longer maintains its own cache: the single
construction at the `ForOperation` call site already guarantees the binding is
built once.

### The invoker

`SmithyOperationInvoker` owns the parts that are *not* protocol-specific: the
middleware pipeline (retry, logging, auth, …) and the transport send. It is
deliberately ignorant of wire formats. The one protocol decision it needs —
"is this response an error?" — is injected as `IOperationProtocol.IsErrorResponse`
rather than assumed to be "HTTP 4xx", so a transport that signals failure
differently (gRPC's `grpc-status` trailer over an HTTP 200) fits without
changing the invoker. Error *dispatch* (which modeled error a response maps to)
stays in the generated `Deserialize{Operation}ErrorAsync` delegate, since the set
of an operation's errors is model data.

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
