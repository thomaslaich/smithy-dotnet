# Schemas, Serialization, and Validation

The runtime schema model is the contract between generated C# model types and
the runtime libraries that consume them: codecs, protocols, and validators.

The generator emits plain model types plus schemas. A schema describes its
shape and, through generated code, serializes values of that shape: it hands
each member to a *shape serializer* and takes each member from a *shape
deserializer*, in calls typed at the member's static type. Everything that
consumes values implements those two interfaces: each codec for its wire format,
the validator to check constraints, and HTTP bindings to route members to
labels, headers, query parameters, and the body. Adding CBOR, XML, JSON, REST,
rpcv2Cbor, or gRPC behavior therefore never requires regenerating
format-specific code into every shape.

## Goals

- **Fidelity.** The schema model mirrors the Smithy meta-model. Anything the
  model can express is representable: member-level traits on list elements and
  map keys, presence semantics, service structure.
- **One path for values.** Values are read and written only through the
  schema's generated `Serialize` and `Deserialize` methods. There is no second,
  interpretive path.
- **No boxing.** Every value crosses into a serializer at its static type.
- **AOT-safe.** Serialization is generated code and plans are plain data. Nothing
  emits IL or reflects over model types.
- **Pay once.** Each consumer builds its per-shape plan once. Nothing is
  resolved per call.

## Layers

1. **Model types** are plain C# values.
2. **Schemas** describe Smithy metadata and serialize values through generated
   methods.
3. **Codecs** implement the shape serializer and deserializer for one wire
   format.
4. **Protocols** bind operation schemas to transport requests and responses.
5. **The validator** implements a shape serializer that checks constraints.

Only the first two layers are generated. Codecs, protocols, and the validator
are runtime libraries configured from the generated schemas. For a worked
example of what generation produces, see the
[Quick Start](https://thomaslaich.github.io/smithy-dotnet/getting-started/quick-start/).

## Generated Model Types

Generated model types are plain C# values. They implement no serialization
interface and hold no serializer callbacks. When deserialization needs staged
construction, the generator emits a separate builder type instead of adding
mutable hooks to the model.

A shape's schema is a sibling class named after the shape, with a single
instance:

```csharp
public sealed record class MenuItem(Food Food, float Price);

public sealed partial class MenuItemSchema : StructSchema<MenuItem, MenuItemSchema.Builder>
{
    public static MenuItemSchema Schema { get; } = new();
    // ...
}
```

The model type carries no reference to its schema. A consumer is handed the
schema rather than deriving it from the value:
`JsonCodecFactory.Default.FromSchema(MenuItemSchema.Schema)`, and a generated
client passes `MenuItemSchema.Schema` to the protocol when it binds an
operation. Every reference is an ordinary static property, so there is no
registry, no reflection, and no startup scan. The schema class is `partial` so
a project can add members to it without touching generated code.

## Schemas

Every shape is a `Schema`, and every aggregate shape is made of *members*,
exactly as in the Smithy model:

- Structures and unions have named members.
- Lists have a single element member.
- Maps have a key member and a value member.

```csharp
public abstract class Schema
{
    public ShapeId Id { get; }
    public ShapeKind Kind { get; }
    public IReadOnlyDictionary<ShapeId, Trait> Traits { get; }
    public IReadOnlyList<Member> Members { get; }
}

public abstract class Schema<T> : Schema
{
    public abstract void Serialize<TSerializer>(T value, ref TSerializer serializer)
        where TSerializer : struct, IShapeSerializer, allows ref struct;

    public abstract T Deserialize<TDeserializer>(ref TDeserializer deserializer)
        where TDeserializer : struct, IShapeDeserializer, allows ref struct;
}
```

The non-generic `Schema` is the whole metadata surface: kind, traits, and
members, which is everything a consumer needs to build its plan. `Schema<T>`
binds a shape to its CLR type `T` and adds the two generated methods, the only
code that touches values.

A scalar is the smallest schema: an id and a kind, no members. The prelude
shapes are therefore shared singletons (`Schemas.String`, `Schemas.Integer`).
One instance serves every integer in every model, because a constraint like
`@range` is declared where the shape is *used*, so it lives on the member.

A structure schema declares its members and implements the generated methods:

```csharp
// structure Rating { @required title: String, @range(min: 1, max: 5) score: Integer, author: User }
public sealed partial class RatingSchema : StructSchema<Rating, RatingSchema.Builder>
{
    public static RatingSchema Schema { get; } = new();

    private RatingSchema()
        : base(
            ShapeId.Parse("example#Rating"),
            [
                new Member("title", Schemas.String, isRequired: true),
                new Member(
                    "score",
                    Schemas.Integer,
                    traits: [new Trait(ShapeId.Parse("smithy.api#range"), Document.From(
                        new Dictionary<string, Document>
                        {
                            ["min"] = Document.From(1),
                            ["max"] = Document.From(5),
                        }))]),
                new Member("author", Schemas.Lazy(() => UserSchema.Schema)),
            ]) { }

    public override void Serialize<TSerializer>(Rating value, ref TSerializer serializer)
    {
        serializer.WriteString(0, value.Title);
        serializer.WriteInt(1, value.Score);
        serializer.WriteStruct(2, value.Author, UserSchema.Schema);
    }

    public override void DeserializeMember<TDeserializer>(
        Builder builder,
        int index,
        ref TDeserializer deserializer)
    {
        switch (index)
        {
            case 0: builder.Title = deserializer.ReadString(); break;
            case 1: builder.Score = deserializer.ReadInt(); break;
            case 2: builder.Author = deserializer.ReadStruct(UserSchema.Schema); break;
        }
    }

    public override Rating Build(Builder builder) =>
        new(
            builder.Title ?? throw new MissingRequiredMemberException("title"),
            builder.Score,
            builder.Author);
}
```

`Build` is where a required member absent from the payload fails, throwing
where the codec can turn it into a modeled error. A defaulted member's builder
property starts at its modeled default, so an absent member keeps it.

For each operation, the generator emits an `OperationSchema<TInput, TOutput>`
that references the input and output schemas, the operation traits, and the
modeled error descriptors. For each service, it emits a `ServiceSchema` with
the service shape id, version, and service-level traits.

Server generation additionally binds each unary operation to JSON Schema
2020-12 input and output documents in its `IServiceOperation`. These describe
NSmithy's canonical JSON document representation (`@jsonName`, epoch-second
timestamps, base64 blobs, unions, and constraints), not any particular wire
protocol, and are what MCP tools consume. Prompt metadata from
`smithy.ai#prompts` becomes transport-neutral `ServicePromptDefinition` values
on the generated `IServiceDefinition`, rendered into MCP prompts by the MCP
adapter. Neither concern touches `ServiceSchema` or `OperationSchema`.

### Members

A member is the association between a container shape and a target shape, and
it is the one place member-level traits live:

```csharp
public sealed class Member
{
    public string Name { get; }
    public int Index { get; }
    public Schema Target { get; }
    public bool IsRequired { get; }

    /// Traits declared on the member itself.
    public IReadOnlyDictionary<ShapeId, Trait> Traits { get; }

    /// Effective trait resolution per the Smithy spec: the member's
    /// declaration supersedes the target shape's. The only place
    /// precedence is implemented.
    public Trait? GetTrait(ShapeId id);
}
```

`Index` is the member's declaration position, the number generated code passes
to the serializer. Only ground truth is stored, each set where the model
declares it: member traits on the member, shape traits on the target.
Effective-value consumers (codecs resolving `@xmlName` or `@timestampFormat`,
the validator reading `@length`) call `GetTrait` and never re-implement
precedence; origin-aware consumers (documentation generation, model diffing)
read `Member.Traits` and `Target.Traits` directly.

A union case, a list element, and a map key and value are members too. Because
map keys are members, a constraint such as `@length` on `map$key` is
representable and validated like any other member trait.

### Recursion

Recursive models form a cyclic schema graph. A schema cannot reference itself
while it is still being constructed, so a member that closes a cycle targets
`Schemas.Lazy(() => NodeSchema.Schema)`, which resolves once on first use and
forwards everything to its target. Generated serialization code references
other schemas directly, so only member metadata needs the indirection.

A consumer cannot walk a cyclic graph eagerly when it builds a plan, so plans
are memoized through `SchemaCompilationCache`, which registers a placeholder for
a shape before building it so a self-reference resolves to the in-progress
plan. The cache keys on the resolved schema, because two references to the same
shape are different objects when one of them is a lazy stand-in.

### Projections

A projection narrows a structure's visible members while keeping the same
container type. REST protocols use one for the body: the members bound to
labels, headers, and query parameters are excluded, and the body codec writes
and reads the rest.

```csharp
var bodyProjection = Schemas.Project(inputSchema, bodyMemberNames);
```

A projection is a member mask evaluated once at construction, so a codec built
for it sees a stable view even when the selection came from a mutable set.

## Traits

Traits are stored as Smithy shape ids plus Smithy `Document` values:

```csharp
public readonly record struct Trait(ShapeId Id, Document Value);
```

Schemas and members carry any Smithy trait; consumers decide which ones they
interpret. REST protocols interpret `@httpLabel`, `@httpHeader`, `@httpPayload`,
and `@timestampFormat`; gRPC ignores HTTP bindings entirely; the validator
interprets constraint traits and nothing else. Each consumer puts its parsing
behind a small helper such as `RestTraits` or `XmlTraits`, so trait ids and
parsing stay centralized while the core model remains open-ended.

## Presence and Nullability

Presence is a property of the *member position*, never of the target shape:

- `required` is member metadata (`Member.IsRequired`).
- Modeled defaults are member traits. Generated builders start defaulted
  members at their default; a codec writing a default reads it from the trait.
- `@sparse` is metadata on the list or map schema, declaring that the
  collection holds nullable elements or values.

Nullability lives in the generated code, not in the schema: an optional
`Integer` member is an `int?` property, and the generated call passes it to
`WriteInt(int index, int? value)`. The serializer decides what a null means
for the member from its plan: omit it, write its default, or write an explicit
null in a sparse collection.

## Serializing Values

Every call names a member of the shape being serialized by its index and passes
the value at its static type. One call carries both, so the serializer decides
in one place whether to write the member, write its default, write an explicit
null, or skip it because it is outside the projection being written.

The serializer and deserializer are `struct` type arguments, so the JIT
compiles the generated methods once per implementation with every call bound
directly: no interface dispatch and no casts per value.

They may also be `ref struct`s, so an implementation can hold a span or a
`Utf8JsonReader` directly; they are always passed by `ref`, never copied or
boxed.

Collections and unions use the same calls. A list element is member 0 of the
list, a map key and value are members 0 and 1, and a union writes its one case
under the case's index:

```csharp
// list SimpleList { member: String }
public override void Serialize<TSerializer>(SimpleList value, ref TSerializer serializer)
{
    foreach (var item in value.Values)
    {
        serializer.WriteString(0, item);
    }
}

public override SimpleList Deserialize<TDeserializer>(ref TDeserializer deserializer)
{
    var items = new List<string>();
    while (deserializer.NextElement())
    {
        items.Add(deserializer.ReadString());
    }

    return SimpleList.FromOwnedList(items);
}
```

The deserializer drives a structure read. It walks its input (JSON properties,
XML elements and attributes, CBOR keys, protobuf field numbers, HTTP headers),
maps each to a member index, and calls the generated `DeserializeMember`, so
input order is irrelevant. `StructSchema<T, TBuilder>.Deserialize` creates the
builder, hands it to `deserializer.ReadStruct`, and finalizes it with `Build`.
A union read works the same way through the union's case reader.

`IShapeSerializer` and `IShapeDeserializer` have one method per simple shape
kind (`WriteString`, `ReadInt`, `WriteTimestamp`, and so on), enum value
methods, `WriteStruct`/`ReadStruct` and their list, map, and union counterparts
for a nested aggregate through its schema, `NextElement` and `NextEntry` for
collections, and `WriteEventStream`/`ReadEventStream` for an event-stream
member (see [streaming.md](streaming.md)). A nested write creates a child
serializer positioned on the nested shape's plan, so generated code never
tracks where it is.

## Plans

Each implementation of `IShapeSerializer` or `IShapeDeserializer` keeps a *plan*:
per-shape tables of the metadata it needs, indexed by member position and
built once from the schema graph. Building one is a walk of the non-generic
schema (`Kind`, `Members`, `Member.Target`, traits) that switches on the shape
kind. Each table links to the tables of its members' targets, so a nested write
switches tables without a lookup, and a recursive shape links back to its own
table.

- A JSON plan holds each member's pre-encoded property name (honoring
  `@jsonName`), timestamp format, and default.
- An XML plan holds element and attribute names, flattening, and namespaces.
- A protobuf plan holds field numbers and packing, and inlines a union's cases
  into the enclosing message.
- The validator's plan holds each member's constraints.
- An HTTP binding plan holds each member's binding: label, header, query
  parameter, payload, status code, or body.

A plan builder rejects a shape kind its format cannot represent, such as an
event stream in a JSON body, when the plan is built rather than when a value is
written.

## Codec Model

A codec implements the shape serializer and deserializer for one wire format.

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

public interface ICodecFactory
{
    ICodec<T> FromSchema<T>(Schema<T> schema, CodecFactoryOptions? options = null);
    ICodec<T> FromMember<T>(Member member, Schema<T> target, CodecFactoryOptions? options = null);
}

public interface IProjectionCodecFactory : ICodecFactory
{
    IProjectionCodec<T, TBuilder> FromProjection<T, TBuilder>(
        StructProjection<T, TBuilder> projection,
        CodecFactoryOptions? options = null);
}
```

```csharp
var personCodec = JsonCodecFactory.Default.FromSchema(PersonSchema.Schema);
var json = personCodec.Serialize(person);
var roundTrip = personCodec.Deserialize(json);

var bodyCodec = JsonCodecFactory.Default.FromProjection(bodyProjection);
var body = bodyCodec.Serialize(input);
```

Each format exposes one factory: `JsonCodecFactory`, `XmlCodecFactory`,
`CborCodecFactory`, and `ProtoCodecFactory`, in the `NSmithy.Codecs.*`
packages. JSON, XML, and CBOR implement `IProjectionCodecFactory`; Protobuf
implements only `ICodecFactory`, because gRPC always encodes complete messages.
`FromMember` applies traits declared on the member, for a payload member whose
traits control its target's wire representation (`@xmlName`,
`@timestampFormat`).

A projection codec reads into a builder rather than producing a value. A
protocol creates one builder per request, lets the HTTP binding deserializer and
the body codec set the members they own, and finalizes the builder once.

Codecs handle body-format concerns: names, timestamp formats, enum values,
sparse collections, documents, and protobuf field numbers. They do not build
HTTP requests, expand URI labels, choose headers, or map status codes; those
belong to protocols.

## Validation

The server enforces the constraint traits: `@required`, `@length`, `@range`,
`@pattern`, `@uniqueItems`, and enum membership. The validator is a shape
serializer: validating a value means serializing it into the validator, whose
plan checks each member's constraints as the value is written and collects
every violation.

```csharp
ISmithyValidator<T>? validator = SmithyValidator.FromSchema(inputSchema);
```

A subgraph with no reachable constraints is skipped, and a schema with no
constraints at all yields no validator, so unconstrained operations pay nothing
per request.

A validator reports all violations in one pass. Each violation carries a JSON
Pointer into the value (`/tags/2`, `/attributes/color`), tracked by the
validator as nested writes descend, and a message, matching the path
`smithy.framework#ValidationExceptionField` documents, so a caller can correct
every problem from a single response.

Generated enum types stay open, so an unrecognized value deserializes rather
than throwing and a client is not broken by a server that added a member. The
server is where that openness stops: the validator checks each written enum
value against the values the schema carries.

`@uniqueItems` compares elements by their canonical encoding: each element is
serialized with the CBOR codec, with map entries sorted by key, and duplicates
are equal byte sequences. Equality is therefore the model's equality, not
.NET's: a blob is a `byte[]` and a generated structure holding a list compares
that member by reference, so .NET equality would let duplicates through.

A missing `@required` member is detected during deserialization, where the
builder is finalized. Every other constraint is checked by the validator after
deserialization and before the handler runs.

### ValidationException

Violations become `smithy.framework#ValidationException`, a modeled error
carrying a message and a `fieldList` of path/message pairs. Every operation
schema carries this error implicitly (a model that declares it explicitly keeps
its own registration), so protocols serialize it like any other modeled error,
and generated clients deserialize it into the typed exception with no special
casing. Handlers never see invalid input.

Clients do not pre-validate inputs; see
[Client-side constraint validation](#client-side-constraint-validation).

## Protocol Model

Protocols bind operation schemas to transports. The interfaces live in
`NSmithy.Http`; [http-interfaces.md](http-interfaces.md) covers how an
`IProtocol` is bound to a service and then to each operation.

```csharp
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
    ISmithyValidator<TInput>? InputValidator { get; }

    ValueTask<TInput> DeserializeRequestAsync(
        SmithyHttpRequest request,
        CancellationToken cancellationToken = default);

    SmithyHttpServerResponse SerializeResponse(
        TOutput output,
        CancellationToken cancellationToken = default);

    bool TrySerializeError(Exception exception, out SmithyHttpServerResponse response);
}
```

Every protocol-specific wire decision lives behind these interfaces, so
generated clients and servers have no protocol-specific branches:

- **Request path.** REST reads the `@http` trait and substitutes labels from
  the input. rpcv2Cbor derives `/service/{Service}/operation/{Operation}` from
  the shape names. gRPC uses `/{package}.{Service}/{Method}`.
- **Body codec.** restJson1 uses JSON, restXml XML, rpcv2Cbor CBOR, and gRPC
  Protobuf.
- **Error discrimination.** `IsErrorResponse` decides whether a response is an
  error at all: the HTTP status for REST and rpcv2Cbor, the `grpc-status`
  trailer for gRPC. REST then reads `X-Amzn-Errortype`, `__type`, or `code`;
  rpcv2Cbor reads `__type` from the CBOR body; gRPC reads a shape-id trailer.

Modeled-error handling is compiled with the operation. A protocol supplies an
`IErrorReaderCompiler` and an `IErrorWriterCompiler`, each a single generic
method that compiles one error with its CLR type in scope;
`HttpOperationError.Compile` and `ModeledErrorSerializer.Compile` apply them to
every modeled error of the operation. `CodecErrorReader` covers protocols whose
error payload is the whole error structure in the body codec's format. Each
protocol implements `DeserializeErrorAsync` by passing its discrimination rules
to the shared `OperationProtocolErrors.DeserializeModeledError` resolver: the
discriminator extractor, whether a discriminator is required (rpc-style
protocols always carry one), and whether the HTTP status may resolve an error
the discriminator did not.

### HTTP bindings

`NSmithy.Protocols.Rest` holds the REST wire format, shared by restJson1 and
restXml, which differ only in the `IRestBodyCodecFactory` they supply.

The REST protocol serializes an operation's input or output through an *HTTP
binding serializer*. Its plan maps each member to its binding, and each write
goes there: a label into the URI template, a header or query parameter through
the HTTP binding value rules below, a status code onto the response, a
`@httpPayload` member through the body codec on its own, and every other member
into the body codec's serializer for the body projection. Deserialization
mirrors it: the HTTP binding deserializer reads labels, headers, and query
parameters into the builder through the generated `DeserializeMember`, and the
body codec reads the body into the same builder.

`RestOperationProtocol` holds the compiled binding plans and error handling for
one operation and delegates to `RestProtocol`, the stateless engine for URI
templating, HTTP binding values, payloads, and error parsing.

### HTTP binding values

HTTP labels, query parameters, and headers are strings formatted by Smithy HTTP
binding rules, not by a body codec:

- Strings and enums are written as their string values.
- Numbers and booleans use Smithy string representations.
- Floats and doubles support `NaN`, `Infinity`, and `-Infinity`.
- Timestamps use the member's timestamp format, defaulting to `http-date` for
  headers and `date-time` elsewhere.
- `@mediaType` strings are base64-encoded.
- Header lists are comma-separated with RFC 7230 quoting and escaping.

## Alternatives Considered

### Consumers as schema folds

Every consumer could walk the schema graph through a generic visitor and compile
a tree of typed nodes: readers and writers for a codec, checkers for the
validator, accessors for HTTP bindings. That needs a schema visitor with a case
per shape kind, a tower of member interfaces typed over container, builder, and
value so each node can read and write its member, and getter and setter
delegates on every member of every generated schema. Each consumer then
reimplements the same walk with its own typed node classes for every shape
kind, and a structure's per-member nodes can only be stored as `object` and
cast back on each value. Rejected: generated serialization methods make the
walk a single generated method per shape, leave each consumer only its own
concern, and keep the schema itself non-generic metadata.

### Fluent schema builders

Schemas could be assembled with a fluent builder, one call per member, as a
convenience over subclassing. Rejected: the serialization methods are generic
over the serializer, and a generic method cannot be a lambda or delegate, so a
builder could describe only metadata, which the base-class constructor already
takes as a member list. Every schema is therefore a class that declares its
members and implements its serialization methods, and tests build theirs from a
Smithy model through the same code generator as any consumer.

### Reflection-based serialization

Scanning properties at runtime via reflection is common in .NET serializers.
Rejected: reflection loses Smithy member metadata unless the model carries
serializer-specific attributes, multiple protocols would need competing
attributes or converters on the same type, and it rules out precomputed plans.

### Format-specific generated serializers

The generator could emit a JSON, CBOR, or XML serializer per shape, as
`System.Text.Json` source generation does. Rejected: it couples generated code
to specific wire formats, adding a codec would require regenerating every
model, and protocol projections would still need to redirect member writes.
The generated serialization methods are format-agnostic: they know only member
indices and CLR types, and every codec uses the same ones.

### Serialization methods on the model type

The model type could implement the serialization interface itself, as
smithy-java's generated shapes implement `SerializableStruct`. Rejected: it
puts serialization members on a type that is otherwise a plain value, and the
schema would still be needed separately for metadata. The schema class owns
both.

### Protocol locations in core schema metadata

Core member metadata could store a normalized location such as `Body`, `Header`,
`Query`, or `Label`. Rejected: one Smithy model serves multiple protocols, and
location is a protocol's interpretation of traits, not a property of the shape.

### An erased object-shaped schema surface

Schemas could expose `object`-typed accessors and builders so heterogeneous
infrastructure can read and construct values without knowing the CLR type.
Rejected: anything built on that surface boxes and casts on the hot path, and
keeping it alongside the generated methods means two behaviors to keep
identical.

### Trait overlays instead of members

List and map elements could be modeled as plain target schemas, with
member-level traits merged onto a wrapping schema at construction. Rejected in
favor of first-class members with a resolution helper, which keeps precedence in
one place while also representing trait origin, map-key traits, and the member
itself as a node. An overlay erases those.

### A materialized merged trait view

Members could carry a third trait collection: the member-over-target merge,
computed at construction. Rejected: trait resolution already runs once per
consumer plan, so caching the merge buys nothing on the hot path, and the merge
cannot represent origin. A member re-declaring a trait with the target's value
is indistinguishable from declaring nothing.

### Presence as nullable wrapper schemas

Optionality could be expressed by wrapping target schemas in nullable adapters.
Rejected: presence is positional in Smithy, so required, optional, and defaulted
all belong to the member, and CLR nullability belongs to the generated code that
reads and writes the value.

### Generated typed trait classes

Generating a CLR type for every Smithy trait would give consumers strongly
typed property access. Rejected: it expands the generated surface, makes vendor
traits a special case, and traits such as `@http` and `@timestampFormat` still
need interpretation by the component that owns their semantics.

### Registry-based schema discovery

A global registry mapping CLR types to schemas supports lookup from
non-generic contexts. Rejected: it requires registration at startup, fails at
runtime rather than compile time, and interacts badly with trimming. Schemas are
referenced through the generated sibling class instead.

### Client-side constraint validation

Clients could validate inputs against constraint traits before serialization.
Rejected: the server is the authority on constraints. A client pinned to an
older model would reject inputs a loosened server-side constraint now allows,
so validation skew punishes exactly the callers who cannot regenerate. Server
enforcement gives every caller, generated client or hand-written request alike,
the same modeled `ValidationException` for the same invalid input, and clients
must handle that error anyway.
