# Serialization

How NSmithy serializes model types to and from wire formats, and why codegen
emits schemas rather than serializers.

Serialization design sits between generated code, runtime libraries, and wire
protocols. Different designs optimize for different things:

- Runtime performance
- Codegen simplicity
- Runtime maintainability
- Extensibility for new protocols and body formats
- Startup time and memory use

NSmithy deliberately keeps generated serialization code small. Codegen is harder
to inspect, test, and evolve than ordinary C# runtime libraries, so the
generator emits plain model types plus schemas, not serializers. It does not
generate protocol-specific serialization branches, and it does not generate
per-shape JSON/XML/CBOR serializer methods.

Schemas are the generated contract between models and the runtime. Codecs
compile format-specific readers and writers from those schemas; protocols
precompute operation bindings from service and operation schemas and compose
them with the appropriate codecs. This shifts some work to startup, but keeps
the hot path typed and precomputed.

That split gives NSmithy's serialization model four layers:

1. **Model types** are plain C# values.
2. **Schemas** describe Smithy metadata, typed member access, and construction.
3. **Codecs** compile schemas into body readers and writers.
4. **Protocols** project operation schemas into transport requests and responses.

Only the first two layers are generated. Codecs and protocols are runtime
libraries that are configured from the generated schemas.

## End-To-End Sketch

A Smithy service defines a protocol, operations, input/output structures, and
traits:

```smithy
$version: "2"

namespace example.weather

use aws.protocols#restJson1

@restJson1
service WeatherService {
    operations: [GetForecast]
}

@http(method: "GET", uri: "/forecast/{city}", code: 200)
operation GetForecast {
    input: GetForecastInput
    output: GetForecastOutput
}

structure GetForecastInput {
    @required
    @httpLabel
    city: String

    @httpQuery("units")
    units: String
}

structure GetForecastOutput {
    @required
    summary: String

    temperature: Integer
}
```

The generator emits model types that do not know how they are serialized:

```csharp
public sealed record GetForecastInput(string City, string? Units);

public sealed record GetForecastOutput(string Summary, int? Temperature);
```

Next to those model types, the generator emits schemas. A structure schema knows
the Smithy shape id, member traits, how to read members from a value, and how to
build the value during deserialization:

```csharp
public static partial class GetForecastInputSchema
{
    public static Schema<GetForecastInput> Schema { get; } =
        Schemas.Struct<GetForecastInput, GetForecastInputBuilder>(
            ShapeId.Parse("example.weather#GetForecastInput"),
            members: [
                Schemas.Member(
                    "city",
                    StringSchema.Schema,
                    get: input => input.City,
                    set: (builder, value) => builder.City = value,
                    traits: [new RequiredTrait(), new HttpLabelTrait()]),

                Schemas.Member(
                    "units",
                    StringSchema.Schema,
                    get: input => input.Units,
                    set: (builder, value) => builder.Units = value,
                    traits: [new HttpQueryTrait("units")])
            ],
            createBuilder: () => new GetForecastInputBuilder(),
            build: builder => new GetForecastInput(builder.City, builder.Units));
}
```

Operations and services have schemas too. They are the entry point for
protocols:

```csharp
public static partial class GetForecastSchema
{
    public static OperationSchema<GetForecastInput, GetForecastOutput> Schema { get; } =
        Schemas.Operation(
            ShapeId.Parse("example.weather#GetForecast"),
            GetForecastInputSchema.Schema,
            GetForecastOutputSchema.Schema,
            traits: [new HttpTrait("GET", "/forecast/{city}", 200)]);
}

public static partial class WeatherServiceSchema
{
    public static ServiceSchema Schema { get; } =
        Schemas.Service(
            ShapeId.Parse("example.weather#WeatherService"),
            traits: [new RestJson1Trait()]);
}
```

A body codec compiles a schema into a reader/writer for JSON, XML, or CBOR. It
does not know about HTTP paths, headers, status codes, or operation routing:

```csharp
var outputCodec = JsonCodec.FromSchema(GetForecastOutputSchema.Schema);

var body = outputCodec.Serialize(new GetForecastOutput("Cloudy", 18));
var output = outputCodec.Deserialize(body);
```

A service protocol interprets service and operation schemas for a concrete
transport. `restJson1` reads the `@http`, `@httpLabel`, and `@httpQuery` traits,
builds the request path, and uses JSON for the body:

```csharp
IServiceProtocol serviceProtocol =
    RestJsonProtocol.ForService(WeatherServiceSchema.Schema);

IOperationProtocol<GetForecastInput, GetForecastOutput> getForecastProtocol =
    serviceProtocol.ForOperation(GetForecastSchema.Schema);

var request = getForecastProtocol.SerializeRequest(
    new GetForecastInput("Berlin", "metric"));

// GET /forecast/Berlin?units=metric
```

Generated clients bind protocols once in static fields. Operation methods stay
protocol-agnostic: they ask the operation protocol to write the request, send it
through the invoker, then ask the operation protocol to read the response:

```csharp
public sealed class WeatherServiceClient
{
    private readonly SmithyOperationInvoker invoker;

    private static readonly IServiceProtocol ServiceProtocol =
        RestJsonProtocol.ForService(WeatherServiceSchema.Schema);

    private static readonly IOperationProtocol<GetForecastInput, GetForecastOutput>
        GetForecastProtocol = ServiceProtocol.ForOperation(GetForecastSchema.Schema);

    public async Task<GetForecastOutput> GetForecastAsync(
        GetForecastInput input,
        CancellationToken cancellationToken = default)
    {
        var request = GetForecastProtocol.SerializeRequest(input);

        var response = await invoker
            .InvokeAsync(
                "WeatherService",
                "GetForecast",
                request,
                DeserializeGetForecastErrorAsync,
                GetForecastProtocol.IsErrorResponse,
                cancellationToken)
            .ConfigureAwait(false);

        return GetForecastProtocol.DeserializeResponse(response);
    }
}
```

For a different protocol, the model types and schemas stay the same. Only the
service protocol factory changes, for example to
`RpcV2CborProtocol.ForService(...)` or `RestXmlProtocol.ForService(...)`.

## Generated Model Types

Generated model types are plain C# values:

```csharp
public sealed record GetWidgetInput(string Id, string? Filter);
```

They do not implement serialization interfaces or contain serializer callbacks.
Wire-format behavior lives in the schema, codec, and protocol layers. When
deserialization needs staged construction, the generator emits a separate
builder type instead of adding mutable hooks to the model.

## Schema Model

Schemas sit next to the model types. A `Schema<T>` (in `NSmithy.Core`) describes
a Smithy shape at runtime. The typed form is used when the C# type is known; the
erased `Schema` base is reserved for shape-generic boundaries, such as protocol
analysis over an operation graph.

For each shape, the generated schema contains typed member accessors, builder
factory, builder finalizer, shape traits, and member traits. For each operation,
the generator emits an `OperationSchema<TInput, TOutput>` that references the
input and output schemas plus operation traits. For each service, it emits a
`ServiceSchema` with the service shape id and service-level traits.

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
```

Projection codecs are used when a protocol wants to serialize only a subset of
the members of a structure while keeping the same container type:

```csharp
var bodyProjection = Schemas.Project(inputSchema, bodyMembers);
var bodyCodec = JsonCodec.FromProjection(bodyProjection);
var body = bodyCodec.Serialize(input);
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

## Protocol Model

Protocols bind operation schemas to transports. The abstraction has two
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

Every protocol-specific wire decision lives behind the implementation, so
generated clients do not need protocol-specific serialization branches:

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

`NSmithy.Protocols.Rest` factors the REST wire format into two pieces:

- `RestOperationProtocol<TInput, TOutput>` — the per-operation `IOperationProtocol`
  implementation. It holds a `RestOperationBinding` and the `IRestBodyFormat`,
  and delegates to the stateless engine.
- `RestProtocol` — the shared stateless wire engine for URI templating,
  label/query/header binding, payload handling, and error parsing. restJson1 and
  restXml share it and differ only by `IRestBodyFormat` (JSON vs XML).

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

A generated client stores protocol objects in `static readonly` fields. The
service protocol is created from the service schema; each operation protocol is
created from its operation schema via `ForOperation`:

```csharp
private static readonly IServiceProtocol ServiceProtocol =
    RpcV2CborProtocol.ForService(RpcV2ProtocolSchema.Schema);

private static readonly IOperationProtocol<GreetingWithErrorsInput, GreetingWithErrorsOutput>
    GreetingWithErrorsProtocol = ServiceProtocol.ForOperation(GreetingWithErrorsSchema.Schema);
```

Operation methods are protocol-agnostic. They have the same shape for restJson1,
restXml, simpleRestJson, and rpcv2Cbor:

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

The `static readonly` fields also define the caching boundary. Work derived from
the operation schema — such as a `RestOperationBinding` for REST or compiled
request/response codecs for rpcv2Cbor — is computed when the field is
initialized and reused for every request. The hot path serializes through those
precomputed objects instead of reanalyzing schema metadata.

### The client runtime

`SmithyClientRuntime` owns the parts that are *not* protocol-specific:
interceptors, auth signing, retry decisions, and the transport send. It is
deliberately ignorant of wire formats. The one protocol decision it needs, "is
this response an error?", is injected as `IOperationProtocol.IsErrorResponse`
rather than assumed to be "HTTP 4xx", so a transport that signals failure
differently (gRPC's `grpc-status` trailer over an HTTP 200) fits without
changing the runtime. Error *dispatch* stays in the generated
`Deserialize{Operation}ErrorAsync` delegate, since the set of modeled errors is
operation-specific model data.

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
