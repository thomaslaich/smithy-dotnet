# Schemas, Serialization, and Validation

The runtime schema model is the contract between generated C# model types and
runtime consumers: codecs, protocols, and validators.

NSmithy deliberately keeps generated serialization code small. The generator
emits plain model types plus schemas, not serializer methods. Codecs and
protocols are ordinary runtime libraries that fold over those schemas, so adding
CBOR, XML, JSON, REST, rpcv2Cbor, or gRPC behavior does not require regenerating
format-specific methods into every shape.

Every runtime capability is a *fold* over the schema algebra: a typed walk of
the schema graph that compiles a plan once and caches it. A codec folds a
schema into body readers and writers; a protocol folds service and operation
schemas into transport bindings; the validator folds a schema into a constraint
checker. Heterogeneous infrastructure can hold a non-generic `Schema` handle,
but behavior re-enters typed code through visitor dispatch.

That split gives NSmithy's model five layers:

1. **Model types** are plain C# values.
2. **Schemas** describe Smithy metadata, typed member access, and construction.
3. **Codecs** compile schemas into body readers and writers.
4. **Protocols** project operation schemas into transport requests and responses.
5. **Validators** compile schemas into constraint checkers.

Only the first two layers are generated. Codecs, protocols, and validators are
runtime libraries configured from the generated schemas.

## Goals

- **Fidelity.** The schema model mirrors the Smithy meta-model. Anything the
  model can express — member-level traits on list elements and map keys,
  presence semantics, service and resource structure — is representable.
- **One dispatch mechanism.** Consumers traverse schemas through a single typed
  visitor. Object-shaped helpers exist for heterogeneous infrastructure, but
  codecs and protocols do not maintain a separate interpretive fallback.
- **Typed everywhere.** Trait values, member access, and construction are all
  statically typed. `object` casts do not appear in steady-state execution.
- **AOT-safe.** Plans are composed from delegates, never emitted IL. The model
  works unchanged under Native AOT.
- **Pay once.** Member lookup tables are built at schema construction; trait
  resolution and consumer plans are computed at fold time. Nothing is resolved
  per call.

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
        Schemas.Structure<GetForecastInput, GetForecastInputBuilder>(
                ShapeId.Parse("example.weather#GetForecastInput"))
            .Required(
                "city",
                input => input.City,
                (builder, value) => builder.City = value,
                Schemas.String,
                traits: [RestTraits.HttpLabelTrait])
            .Optional(
                "units",
                input => input.Units,
                (builder, value) => builder.Units = value,
                Schemas.NullableReference(Schemas.String),
                traits: [RestTraits.HttpQueryTrait("units")])
            .Build(
                () => new GetForecastInputBuilder(),
                builder => new GetForecastInput(builder.City!, builder.Units));
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
            traits: [RestTraits.HttpTrait("GET", "/forecast/{city}")]);
}

public static partial class WeatherServiceSchema
{
    public static ServiceSchema Schema { get; } =
        Schemas.Service(
            ShapeId.Parse("example.weather#WeatherService"),
            traits: [new Trait(ShapeId.Parse("aws.protocols#restJson1"))]);
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
    new RestJson1Protocol().ForService(WeatherServiceSchema.Schema);

IOperationProtocol<GetForecastInput, GetForecastOutput> getForecastProtocol =
    serviceProtocol.ForOperation(GetForecastSchema.Schema);

var request = getForecastProtocol.SerializeRequest(
    new GetForecastInput("Berlin", "metric"));

// GET /forecast/Berlin?units=metric
```

Generated clients precompute one operation binding per operation in the
constructor. Operation methods stay protocol-agnostic: they hand the bound
operation and typed input to the shared client runtime:

```csharp
public sealed class WeatherServiceClient
{
    private readonly SmithyClientRuntime runtime;

    private readonly SmithyOperationBinding<GetForecastInput, GetForecastOutput>
        GetForecastBinding;

    public WeatherServiceClient(/* endpoint / config / runtime */)
    {
        IServiceProtocol serviceProtocol =
            new RestJson1Protocol().ForService(WeatherServiceSchema.Schema);

        GetForecastBinding = new SmithyOperationBinding<GetForecastInput, GetForecastOutput>(
            WeatherServiceSchema.Schema.Id,
            GetForecastSchema.Schema.Id,
            serviceProtocol.ForOperation(GetForecastSchema.Schema));
        // ... one binding per operation, plus runtime construction
    }

    public async Task<GetForecastOutput> GetForecastAsync(
        GetForecastInput input,
        CancellationToken cancellationToken = default)
    {
        return await runtime
            .InvokeAsync(GetForecastBinding, input, cancellationToken)
            .ConfigureAwait(false);
    }
}
```

The binding carries the service and operation shape ids and the bound
operation protocol. The runtime owns execution — interceptors, auth, retries —
and modeled-error deserialization runs through the binding's protocol, so the
generated method has no protocol-specific branches.

On the server, the runtime deserializes the request through the same operation
protocol, validates the input against the model's constraint traits, and
invokes the handler; constraint violations never reach handler code (see
[Validation](#validation)).

For a different protocol, the model types and schemas stay the same. Only the
protocol instance changes, for example to `new RpcV2CborProtocol()` or
`new RestXmlProtocol()`.

## Generated Model Types

Generated model types are plain C# values:

```csharp
public sealed record GetWidgetInput(string Id, string? Filter);
```

They do not implement serialization interfaces or contain serializer callbacks.
Wire-format behavior lives in the schema, codec, and protocol layers. When
deserialization needs staged construction, the generator emits a separate
builder type instead of adding mutable hooks to the model.

## The Schema Algebra

Every shape is a `Schema`, and every aggregate shape is made of *members*,
exactly as in the Smithy model:

- Structures and unions have named members.
- Lists have a single `Member`.
- Maps have `Key` and `Value` members.

```csharp
public abstract class Schema
{
    public ShapeId Id { get; }
    public ShapeKind Kind { get; }
    public IReadOnlyDictionary<ShapeId, Trait> Traits { get; }
}

public abstract class Schema<T> : Schema;
```

`Schema<T>` binds a shape to its exact CLR type `T`, including nullability
annotations. The typed layer is the only real layer; there is no erased
counterpart.

For each shape, the generated schema contains typed member accessors, builder
factory, builder finalizer, shape traits, and member traits. For each
operation, the generator emits an `OperationSchema<TInput, TOutput>` that
references the input and output schemas plus operation traits and modeled
error descriptors. For each service, it emits a `ServiceSchema` with the
service shape id and service-level traits.

Traits stay on schemas and members. Core schemas carry any Smithy trait, but
consumers decide which traits they interpret. REST protocols interpret
`@httpLabel`, `@httpHeader`, `@httpPayload`, and `@timestampFormat`; a gRPC
protocol can ignore those bindings entirely; the validator interprets
constraint traits and nothing else.

### The Non-Generic `Schema`

The non-generic `Schema` exists because C# lacks existential types: it is the
encoding of `exists T. Schema<T>`, nothing more. Three positions force it:

- **Heterogeneous edges in the graph.** A structure's members list holds
  members whose value types all differ, an error registry maps `ShapeId` to a
  schema of unknown type, and a server dispatch table holds operation schemas
  with varying type arguments. Each needs a common supertype to be stored at
  all, and `IMemberSchema.Target` needs a type to return.
- **Re-entry into typed land.** From an untyped handle, the only way back to
  `Schema<T>` is double dispatch: `schema.Accept(visitor)`, where the sealed
  leaf calls the visitor with its own type arguments.
- **Metadata reads without dispatch.** Documentation, diffing, and logging
  read `Id`, `Kind`, or `Traits` off an arbitrary node without constructing a
  visitor.

That list is exhaustive, and none of it is behavior. The non-generic surface
is exactly `Id`, `Kind`, `Traits`, and `Accept` — an existential wrapper plus
metadata. Construction, member access, and serde exist only on the typed
layer, reachable only through the visitor.

Entry points never touch the untyped handle: anywhere traversal starts from a
known shape (`ISmithyShape<T>.Schema`, a generated operation), it is in
`Schema<T>` from the first instruction, and a fold stays typed all the way
down — inside `Visit<TValue>` the recursion continues through `TargetSchema`,
which is `Schema<TValue>`. The untyped base appears only at graph-internal
heterogeneous positions and in generic tooling, and it is held only until the
next `Accept`.

### Members

A member is the association between a container shape and a target shape, and
it is the one place member-level traits live:

```csharp
public interface IMemberSchema
{
    string Name { get; }

    /// Traits declared on the member itself.
    IReadOnlyDictionary<ShapeId, Trait> MemberTraits { get; }

    Schema Target { get; }

    /// Effective trait resolution per the Smithy spec: the member's
    /// declaration supersedes the target shape's. The only place
    /// precedence is implemented.
    Trait? GetTrait(ShapeId id);
}
```

Only ground truth is stored, each set where the model declares it: member
traits on the member, shape traits on the target. Effective-value consumers
(codecs resolving `@xmlName` or `@timestampFormat`, validators reading
`@length`) call `GetTrait(ShapeId)` and never re-implement precedence. Since
consumers are compiled folds, resolution runs at fold time — once per
consumer and schema — so no merged view is materialized. Origin-aware
consumers (documentation generation, model diffing, hierarchical trait
queries) read `MemberTraits` and `Target.Traits` directly.

Structure members additionally carry typed accessors, a typed setter for the
builder, and required-member metadata:

```csharp
public interface IMemberSchema<TContainer, TBuilder, TValue>
    : IMemberSchema<TContainer, TValue>
{
    Schema<TValue> TargetSchema { get; }
    TValue GetValue(TContainer container);
    void SetValue(TBuilder builder, TValue value);
}
```

List and map members expose typed collection-member schemas, not container
accessors: an element is enumerated and appended, never read by member name.
Because map keys are members, a constraint such as `@length` on `map$key` is
representable and validated like any other member trait.

### Recursion

Recursive models form a cyclic schema graph. Generated schemas use
`Schemas.Lazy(...)` to tie the cycle, and `LazySchema<T>` resolves to the target
schema before dispatching to a visitor. Consumers see the same typed schema
interface either way.

### Projections

REST protocols use projections to keep the same container type while narrowing
the visible member set:

```csharp
var bodyProjection = Schemas.Project(inputSchema, bodyMembers);
```

The projection itself is just schema metadata: it says which members of the
container are visible for a particular protocol body. The actual codec compiled
from that projection is introduced in the codec layer below.

## Traits

Traits are stored as Smithy shape ids plus Smithy `Document` values:

```csharp
public readonly record struct Trait(ShapeId Id, Document Value);
```

Schemas and members keep traits in `IReadOnlyDictionary<ShapeId, Trait>`, and
members expose `GetTrait(ShapeId)` for effective trait resolution. Protocols and
codecs put format-specific parsing behind small helper APIs such as
`RestTraits.HttpTrait(...)` and `XmlTraits`, so trait ids and wire-shape parsing
are centralized even though the core schema model remains open-ended.

## Dispatch: Folds Over the Algebra

There is exactly one way to consume a schema: a generic visitor covering the
whole algebra.

```csharp
public interface ISchemaVisitor<out TResult>
{
    TResult VisitBoolean(Schema<bool> schema);
    TResult VisitString(Schema<string> schema);
    TResult VisitTimestamp(Schema<DateTimeOffset> schema);
    // ... remaining simple shapes ...

    TResult VisitNullable<T>(NullableSchema<T> schema)
        where T : struct;

    TResult VisitList<TCollection, TElement, TBuilder>(
        IListSchema<TCollection, TElement, TBuilder> schema);

    TResult VisitMap<TDictionary, TValue, TBuilder>(
        IMapSchema<TDictionary, TValue, TBuilder> schema);

    TResult VisitStruct<T, TBuilder>(IStructSchema<T, TBuilder> schema);
    TResult VisitUnion<T>(IUnionSchema<T> schema);
}
```

A consumer is a *fold*: it visits the schema graph once and compiles a typed
plan, cached per (consumer, schema).

- A codec folds a schema into `Writer<T>` and `Reader<T>` delegates.
- A protocol folds service and operation schemas into transport bindings.
- The validator folds a schema into a `Validator<T>`.
- A documentation generator folds a schema into rendered docs.
- A test-data generator folds a schema into an `Arbitrary<T>`.

The hot path executes only precompiled, monomorphized delegates: no boxing, no
per-value trait lookup, no runtime type tests. Because plans are delegate
composition rather than emitted IL, the same machinery runs under Native AOT.

Generic dispatch through the visitor is the *only* dispatch. Adding a consumer
means writing one fold; adding a shape kind means every fold fails to compile
until it handles the new case — exhaustiveness is enforced by the type system
rather than by runtime `default:` branches.

## Shape–Schema Binding

Generated shapes know their own schemas through a static abstract interface
member:

```csharp
public interface ISmithyShape<TSelf>
{
    static abstract Schema<TSelf> Schema { get; }
}
```

Schema discovery is a generic constraint — `Serialize<T>(T value) where T :
ISmithyShape<T>` — with no registry, no reflection, and no startup scan. Any
API that has the type has the schema.

Construction during deserialization goes through the schema as well: a struct,
list, or map schema exposes a typed builder protocol (create, set/add, build)
that folds compile against. The builder type appears in the schema visitor only
while compiling the plan; public codec entry points remain typed by the model
value.

## Presence and Nullability

Presence is a property of the *member position*, never of the target shape:

- `required` is member metadata (`IMemberSchema.IsRequired`).
- Modeled defaults are represented as member traits and interpreted by codecs
  and validators where needed.
- `@sparse` is metadata on the list or map schema, declaring that the
  collection holds nullable elements or values.
- `Schema<T>`'s type argument is always the exact CLR type, including
  nullability annotations, so an optional integer member targets
  `NullableSchema<int>` through `Schemas.Nullable(Schemas.Integer)`.

Reference-type nullability stays in the `Schema<T>` type argument; value-type
nullability uses `NullableSchema<T>` because C# needs a distinct runtime type
for `T?`. A fold reads required/default metadata from the member and nullable
value-type metadata from the schema.

## Codec Model

A codec is the serialization fold: compiled once from a schema, producing a
tree of typed reader and writer objects that mirror the schema graph. On the
hot path, no schema dispatch or trait lookup occurs; those decisions are baked
into the compiled reader/writer tree.

```csharp
public interface ICodec<TValue>
{
    byte[] Serialize(TValue value);
    TValue Deserialize(byte[] payload);
}

public interface IProjectionCodec<TValue, in TBuilder>
{
    byte[] Serialize(TValue value);
    void ReadInto(byte[] payload, TBuilder builder);
}
```

A protocol creates one typed builder per request, lets each projection codec and
each HTTP binding reader write the members it owns, and finalizes the builder
once.

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

Codecs read schema metadata at fold time. They handle body-format traits such
as XML names, timestamp formats, enum values, sparse collections, and document
nodes. They do not build HTTP requests, expand URI labels, choose headers, or
map status codes; those are protocol responsibilities.

### Codec Compilation

`JsonCodec.FromSchema<T>` folds the schema graph once and produces an
`IJsonValueReader<T>` / `IJsonValueWriter<T>` tree. Each node in the tree is a
small sealed class with the exact concrete types it needs captured in its
generic parameters — including the builder type, which the fold obtains
through the schema's builder plan.

For a structure member typed `int`:

```csharp
JsonMemberReader<TContainer, TBuilder, int>
  .ReadInto(TBuilder builder, JsonElement element)
      value = IntegerJsonValueReader.Read(element)   // element.GetInt32() -> int
      setValue(builder, value)                       // Action<TBuilder, int>
```

For a list typed `IReadOnlyList<string>`:

```csharp
ListJsonValueReader<IReadOnlyList<string>, string, List<string>>
  .Read(JsonElement element)
      builder = plan.CreateBuilder()                 // new List<string>()
      for each element:
          plan.Add(builder, reader.Read(element))    // string
      return plan.Build(builder)                     // typed collection
```

The entire body deserialization path for value types is boxing-free.

## Validation

Constraint traits — `@required`, `@length`, `@range`, `@pattern`,
`@uniqueItems` — are enforced by the server. Validation is a fold like any
other: it compiles a schema into a `Validator<T>` that walks a deserialized
value and collects every violation.

```csharp
Validator<T>? validator = SmithyValidator.FromSchema(inputSchema);
```

The fold resolves each constraint through member precedence
(`member.GetTrait(LengthTraitId)`), reads `@required` off
`IMemberSchema.IsRequired`, and compiles one checker per constrained node. It
prunes aggressively: a
subgraph with no reachable constraints compiles to nothing, and a schema with
no constraints at all yields no validator, so unconstrained operations pay
zero — no wrapper, no walk, no branch per request.

A validator reports all violations in one pass rather than failing on the
first. Each violation carries a path into the value (`$.tags[2]`,
`$.attributes.color`) and a message, so a caller can correct every problem
from a single response.

On the server request path, the validation fold fuses with the codec's reader
fold: constraint checks run as the value is built, so the input is
deserialized and validated in a single pass with no second walk over the
value. Missing `@required` members are detected where the builder is
finalized, in the same pass. The standalone `Validator<T>` remains the form
used to validate values that did not arrive through a codec.

### ValidationException

The server runtime runs the input validator between request deserialization
and the handler. Violations become `smithy.framework#ValidationException`, a
modeled error carrying a message and a `fieldList` of path/message pairs.
Every operation schema carries this error implicitly — a model that declares
it explicitly keeps its own registration — so protocols serialize it exactly
like any other modeled error, and generated clients deserialize it into the
typed exception with no special casing.

Clients do not pre-validate inputs. The server is the authority on
constraints: when a service loosens a constraint, deployed clients benefit
immediately rather than rejecting inputs a newer model allows, and every
caller — generated client or hand-written request — receives the same modeled
error for the same invalid input. Handlers never see invalid input, and
handler authors write no constraint checks.

## Protocol Model

Protocols bind operation schemas to transports. The abstraction lives in
`NSmithy.Http`:

```csharp
public interface IProtocol
{
    IServiceProtocol ForService(ServiceSchema service);
    bool RequiresHttp2 => false;
}

public interface IServiceProtocol
{
    IOperationProtocol<TInput, TOutput> ForOperation<TInput, TOutput>(
        OperationSchema<TInput, TOutput> operation);
}

public interface IClientOperationProtocol<TInput, TOutput>
{
    SmithyHttpRequest SerializeRequest(
        TInput input,
        CancellationToken cancellationToken = default);

    ValueTask<TOutput> DeserializeResponseAsync(
        SmithyHttpClientResponse response,
        CancellationToken cancellationToken = default);

    bool IsErrorResponse(SmithyHttpClientResponse response);

    ValueTask<Exception?> DeserializeErrorAsync(
        SmithyHttpClientResponse response,
        CancellationToken cancellationToken = default);
}

public interface IServerOperationProtocol<TInput, TOutput>
{
    ValueTask<TInput> DeserializeRequestAsync(
        SmithyHttpRequest request,
        CancellationToken cancellationToken = default);

    SmithyHttpServerResponse SerializeResponse(
        TOutput output,
        CancellationToken cancellationToken = default);

    bool TrySerializeError(Exception exception, out SmithyHttpServerResponse response);
}

// Protocol implementations implement the combined interface; client-side code
// (operation bindings, the client runtime) depends only on the client half and
// server-side code only on the server half.
public interface IOperationProtocol<TInput, TOutput>
    : IClientOperationProtocol<TInput, TOutput>,
      IServerOperationProtocol<TInput, TOutput>;
```

Modeled-error handling is precomputed: each protocol compiles its operation's
modeled errors into `HttpOperationError` deserializers and implements
`DeserializeErrorAsync` by composing the shared
`OperationProtocolErrors.DeserializeModeledError` resolver with its own
discrimination rules — the discriminator extractor, whether a discriminator is
required (rpc-style protocols always carry one), and whether the HTTP status
code may resolve an error when the discriminator does not (REST). Those rules
are per-protocol internals, not interface members.

Each protocol implements `IProtocol.ForService(ServiceSchema)`, returning an
`IServiceProtocol`, which in turn hands out an `IOperationProtocol` per
operation. Protocol instances can carry configuration such as gRPC limits or
HTTP/2 requirements, while service and operation protocols carry schema-derived
plans.

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

A `RestOperationBinding<TInput, TOutput, TInputBuilder, TOutputBuilder>`
precomputes everything determinable from the operation schema before any request
arrives — HTTP method, URI template, the label/header/query/queryParams/payload
member lists, and the input/output body projections.
`SerializeRequest`/`DeserializeRequestAsync` then iterate those precomputed
lists directly; no trait lookup, LINQ, or schema-analysis allocation is needed
per request.

rpcv2Cbor is **not** built on `RestProtocol`. Its `IOperationProtocol`
implementation holds the request URI plus the request/response CBOR codecs
(with the per-direction default-materialization policy baked in) and writes the
CBOR envelope directly.

## Client Generation

A generated client resolves the protocol once in its constructor, binds it to
the service schema, then creates one client-runtime binding per operation:

```csharp
private readonly SmithyOperationBinding<GreetingWithErrorsInput, GreetingWithErrorsOutput>
    greetingWithErrorsBinding;

public RpcV2ProtocolClient(/* endpoint / config / runtime */)
{
    var resolvedProtocol = config.Protocol ?? new RpcV2CborProtocol();
    var serviceProtocol = resolvedProtocol.ForService(RpcV2ProtocolSchema.Schema);

    greetingWithErrorsBinding =
        new SmithyOperationBinding<GreetingWithErrorsInput, GreetingWithErrorsOutput>(
        RpcV2ProtocolSchema.Schema.Id,
        GreetingWithErrorsSchema.Schema.Id,
        serviceProtocol.ForOperation(GreetingWithErrorsSchema.Schema));
}
```

Operation methods are protocol-agnostic. They have the same shape for restJson1,
restXml, simpleRestJson, and rpcv2Cbor:

```csharp
public async Task<GreetingWithErrorsOutput> GreetingWithErrorsAsync(
    GreetingWithErrorsInput input,
    CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(input);
    return await runtime.InvokeAsync(greetingWithErrorsBinding, input, cancellationToken)
        .ConfigureAwait(false);
}
```

Those constructor-created bindings define the caching boundary. Work derived
from the operation schema — such as a `RestOperationBinding` for REST or
compiled request/response codecs for rpcv2Cbor — is computed when the client is
constructed and reused for every request. The hot path serializes through those
precomputed objects instead of reanalyzing schema metadata.

The generated client also precomputes a client-runtime operation binding per
unary operation. That binding pairs the service and operation shape ids with
the operation-bound protocol. The operation-bound protocol owns modeled error
deserialization and applies request-mutating traits (`@requestCompression`,
`@httpChecksumRequired`) during serialization, compiled once from the
operation schema's traits; operation schemas carry the modeled error
descriptors it uses for dispatch. Protocol implementations compile those descriptors into
protocol-specific error deserializers when the operation protocol is built, so
per-call generated code stays thin and error codec construction stays off the
deserialization path.

### The client runtime

`SmithyClientRuntime` owns the parts that are *not* protocol-specific:
interceptors, auth signing, retry decisions, and the transport send. It is
deliberately ignorant of wire formats. Protocol decisions such as "is this
response an error?" and "which modeled exception does this response represent?"
come from the operation-bound protocol rather than runtime HTTP assumptions, so
a transport that signals failure differently (gRPC's `grpc-status` trailer over
an HTTP 200) fits without changing the runtime.

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

## The Full Model at Runtime

The schema model covers the entire Smithy model, not only data shapes:

```csharp
public sealed class ServiceSchema
{
    public ShapeId Id { get; }
    public IReadOnlyDictionary<ShapeId, Trait> Traits { get; }
    public IReadOnlyList<OperationSchema> Operations { get; }
    public IReadOnlyList<ResourceSchema> Resources { get; }
}
```

Operations carry input, output, error schemas, and typed traits. Because the
full graph is available with typed traits, generic infrastructure is a fold
over the model rather than a codegen feature:

- Paginators and waiters derive from `@paginated` and waitable traits.
- Auth middleware derives from auth traits on services and operations.
- Protocol conformance tests generate from `@httpRequestTests` /
  `@httpResponseTests`.
- Mock servers and live documentation endpoints derive from the same graph the
  real server dispatches on.

## Compile-Time Plans

The fold that a codec or validator runs at first use can equally run at build
time. A source generator in the consuming project executes the same fold over
generated schemas and emits the resulting plans as ordinary C#, reducing
startup cost to zero for known protocols. Model types stay wire-format-free —
the plans live with the consumer, not the model — and adding a codec never
touches generated models. Runtime folding remains for dynamically constructed
schemas (documents, generic tooling) and for codecs the generator does not
know about. The fold is written once; where it runs is deployment detail.

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

### Model-side per-format serializers

The Java codegen could emit typed JSON deserializers alongside each shape,
similar to `System.Text.Json` source generation.

**Rejected because:**

- It couples generated model types to a specific wire format.
- Adding a new codec (CBOR, XML, MessagePack) would require regenerating all
  models.
- The fold achieves boxing-free deserialization without per-format codegen,
  while keeping codecs swappable at runtime.

This is distinct from [compile-time plans](#compile-time-plans), where a
consumer-side source generator runs the same fold at build time: there the
plans live in the consuming project, models stay format-free, and new codecs
require no model regeneration.

### Protocol locations in core schema metadata

Core member metadata could store a normalized location such as `Body`, `Header`,
`Query`, or `Label`.

**Rejected because:**

- A single Smithy model can be used by multiple protocols.
- Location is a protocol interpretation of traits, not an intrinsic property of
  the shape.
- Sharing traits lets protocols interpret them independently without forcing
  agreement on one projection.

### An interpretive erased codec path

Every schema exposes enough `object`-shaped members for heterogeneous
infrastructure to store, inspect, and build values when the CLR type is not
known statically. Using that surface as the main codec implementation was
rejected: it would box and cast on the hot path and would force compiled and
interpretive serializers to stay behaviorally identical. Runtime codecs use the
erased surface only to re-enter typed visitor dispatch and compile typed plans.

### Trait overlays instead of members

List and map elements can be modeled as plain target schemas, with member-level
traits merged onto a wrapping schema at construction. This keeps every consumer
input a single `Schema` and computes precedence once. Rejected in favor of
first-class members with a resolution helper, which keeps precedence in one
place while also representing trait origin, map-key traits, and the member
itself as a first-class node — capabilities an overlay erases.

### A materialized merged trait view

Members can carry a third trait collection: the member-over-target merge,
computed at construction, so effective-value consumers read one dictionary.
Rejected: because consumers are compiled folds, trait resolution already runs
once per consumer and schema, so caching the merge buys nothing on the hot
path. The stored view also cannot represent origin — a member re-declaring a
trait with a value equal to the target's is indistinguishable from declaring
nothing — so it cannot replace the split views, only duplicate them.
`GetTrait(ShapeId)` provides the same single point of precedence without the
redundant storage.

### Presence as nullable wrapper schemas

Optionality can be expressed entirely by wrapping target schemas in nullable
adapters. Rejected: presence is positional in Smithy, so `required`, optional,
and defaulted all belong to the member, not to the target shape. NSmithy only
uses `NullableSchema<T>` where C# requires a distinct runtime type for nullable
value types; it does not use nullability wrappers as the source of member
presence.

### Generated typed trait classes

Generating a CLR type for every Smithy trait would give consumers strongly
typed property access. Rejected for now: it expands the generated surface, makes
vendor traits a special case, and still requires protocol-specific
interpretation for traits such as `@http` and `@timestampFormat`. The core model
stores all traits as `Trait(ShapeId, Document)` and centralizes parsing in the
runtime component that owns the trait's semantics.

### Registry-based schema discovery

A global registry mapping CLR types to schemas supports lookup from
non-generic contexts. Rejected: it requires registration at startup, fails at
runtime rather than compile time, and interacts badly with trimming. Static
abstract interface members provide discovery as a compile-time constraint.

### Client-side constraint validation

Clients could validate inputs against constraint traits before serialization.
Rejected: the server is the authority on constraints. A client pinned to an
older model would reject inputs a loosened server-side constraint now allows,
so validation skew punishes exactly the callers who cannot regenerate. Server
enforcement gives every caller — generated client or hand-written request —
the same modeled `ValidationException` for the same invalid input, and clients
must handle that error anyway.
