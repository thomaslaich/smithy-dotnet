# Schemas, Serialization, and Validation

The runtime schema model is the contract between generated C# model types and
the runtime libraries that consume them: codecs, protocols, and validators.

The generator emits plain model types plus schemas. A schema describes its
shape and carries a generated, format-agnostic *shape serializer* that moves
values in and out of a typed writer or reader. A codec supplies that writer and
reader for one wire format, so serialization is typed calls from generated code
into the codec. Everything else that consumes schemas (protocols, the validator,
documentation) is a *fold*: a typed walk of the schema graph that compiles a
plan once and caches it. Adding CBOR, XML, JSON, REST, rpcv2Cbor, or gRPC
behavior therefore never requires regenerating format-specific code into every
shape.

## Goals

- **Fidelity.** The schema model mirrors the Smithy meta-model. Anything the
  model can express is representable: member-level traits on list elements and
  map keys, presence semantics, service structure.
- **One path per concern.** Values cross a codec through generated shape
  serializers only; schemas are traversed through a single typed visitor. Neither
  has an interpretive fallback.
- **No boxing.** Trait values, member access, and construction are statically
  typed, so a compiled plan moves values of any type without boxing them.
- **AOT-safe.** Plans are composed from delegates, never emitted IL. The model
  works unchanged under Native AOT.
- **Pay once.** Member lookup tables are built at schema construction; trait
  resolution and consumer plans are computed at fold time. Nothing is resolved
  per call.

## Layers

1. **Model types** are plain C# values.
2. **Schemas** describe Smithy metadata, typed member access, and construction,
   and carry the generated shape serializers.
3. **Codecs** implement typed writers and readers for one wire format.
4. **Protocols** project operation schemas into transport requests and responses.
5. **Validators** compile schemas into constraint checkers.

Only the first two layers are generated. Codecs, protocols, and validators are
runtime libraries configured from the generated schemas. For a worked example of
what generation produces, see the
[Quick Start](https://thomaslaich.github.io/smithy-dotnet/getting-started/quick-start/).

## Generated Model Types

Generated model types are plain C# values. They implement no serialization
interface and hold no serializer callbacks. When deserialization needs staged
construction, the generator emits a separate builder type instead of adding
mutable hooks to the model.

A shape's schema lives on a sibling static class named after the shape:

```csharp
public sealed record class MenuItem(Food Food, float Price);

public static partial class MenuItemSchema
{
    public static Schema<MenuItem> Schema { get; } = /* ... */;
}
```

The model type carries no reference to its schema. A consumer is handed the
schema rather than deriving it from the value:
`JsonCodecFactory.Default.FromSchema(MenuItemSchema.Schema)`, and a generated
client passes `MenuItemSchema.Schema` to the protocol when it binds an
operation. One shape references another's schema by naming the sibling class
(`Schemas.Lazy(() => FoodSchema.Schema)`). Every reference is an ordinary static
property, so there is no registry, no reflection, and no startup scan. The
schema class is `partial` so a project can add members to it without touching
generated code.

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
annotations. The typed layer is the only real layer.

The non-generic `Schema` is a storage handle, not a second API. Some positions
hold shapes whose type arguments all differ and need a common supertype to be
stored at all: a structure's member list, an error registry keyed by `ShapeId`,
a server's dispatch table. Its entire surface is `Id`, `Kind`, `Traits`, and
`Accept`; construction, member access, and serde exist only on `Schema<T>`,
which `schema.Accept(visitor)` gets back to.

A scalar is the smallest node in the algebra: an id and a kind, no members and
no accessors. The prelude shapes are therefore shared singletons:

```csharp
public static Schema<int> Integer { get; } =
    new IntegerSchema(new ShapeId("smithy.api", "Integer"));
```

One instance serves every integer in every model. A constraint like `@range` is
declared where the shape is *used*, so it lives on the member rather than on a
private copy of the integer schema.

A structure schema names its members, pairs each with the schema of its target,
and closes with the two halves of construction: how to make a builder and how to
finalize one:

```csharp
// structure Rating { @required title: String, @range(min: 1, max: 5) score: Integer }
public static Schema<Rating> Schema { get; } =
    Schemas.Structure<Rating, Builder>(ShapeId.Parse("example#Rating"))
        .Required(
            "title",
            static value => value.Title,
            static (builder, value) => builder.Title = value,
            Schemas.NullableReference(Schemas.String))
        .Optional(
            "score",
            static value => value.Score,
            static (builder, value) => builder.Score = value,
            Schemas.Nullable(Schemas.Integer),
            [new Trait(ShapeId.Parse("smithy.api#range"), Document.From(
                new Dictionary<string, Document>
                {
                    ["min"] = Document.From(1),
                    ["max"] = Document.From(5),
                }))])
        .Build(
            static () => new Builder(),
            static builder => new Rating(
                builder.Title ?? throw new MissingRequiredMemberException("title"),
                builder.Score),
            new Serializer());
```

The accessors are delegates over the concrete type, so reading and writing a
member costs no reflection and no boxing. The finalizer is where a required
member absent from the payload fails, throwing where the codec can turn it into
a modeled error. `Serializer` is the generated shape serializer; see
[Shape Serializers](#shape-serializers).

Construction style follows how many typed parts a shape kind has. A shape with
none is a constructor (`new IntegerSchema(id)`). A shape whose parts are fixed
by its kind is a factory method: `Schemas.List(id, element)` takes exactly one
element schema and `Schemas.Map(id, value)` exactly one value schema. Only a
shape with an unbounded number of *differently typed* parts is fluent, which
means structures and unions.

The fluency is there for type inference. Each `.Required` or `.Optional` call
infers its own `TValue` from the accessor pair and the target schema, and stores
a `MemberSchema<T, TBuilder, TValue>` that keeps the member's value type. A
constructor taking a member collection would erase `TValue`, so every member
would have to spell out `MemberSchema<Rating, Builder, string>` at the call
site. The builder also derives each member id from the shape id, so a member
cannot be given an id that disagrees with the shape it belongs to.

For each operation, the generator emits an `OperationSchema<TInput, TOutput>`
that references the input and output schemas, the operation traits, and the
modeled error descriptors. It implements `IOperationSchema`, a non-generic view
of the same metadata for heterogeneous consumers. For each service, the
generator emits a `ServiceSchema` with the service shape id, version, and
service-level traits.

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
consumers are compiled folds, resolution runs once per consumer and schema, so
no merged view is materialized. Origin-aware consumers (documentation
generation, model diffing) read `MemberTraits` and `Target.Traits` directly.

What every member has in common is its target's static type, which is what a
consumer needs to compile anything per member. That is one interface, shared by
structure members, list elements, and map keys and values:

```csharp
public interface ITypedTargetMemberSchema<TValue> : IMemberSchema
{
    Schema<TValue> TypedTarget { get; }
}
```

A structure member additionally reads and writes its value on a container,
which a collection member cannot do. Those accessors are typed over the
container and its builder, so a plan compiled for a member moves the value
without boxing it. A consumer that only knows the container reaches the value
type through the member visitor:

```csharp
public interface IMemberSchema<TContainer, TValue>
    : IMemberSchema<TContainer>, ITypedTargetMemberSchema<TValue>
{
    TValue GetValue(TContainer container);
}

public interface IMemberSchema<TContainer, TBuilder, TValue>
    : IMemberSchema<TContainer, TValue>, IBuilderMemberSchema<TContainer, TBuilder>
{
    void SetValue(TBuilder builder, TValue value);
}

public interface IMemberVisitor<TContainer, TBuilder>
{
    void Visit<TValue>(IMemberSchema<TContainer, TBuilder, TValue> member);
}
```

A union case is a member too, and carries a member id, so a consumer reports a
case the way it reports any other member.

List and map members expose typed collection-member schemas, not container
accessors: an element is enumerated and appended, never read by member name.
Because map keys are members, a constraint such as `@length` on `map$key` is
representable and validated like any other member trait.

### Recursion

Recursive models form a cyclic schema graph. A schema cannot reference itself
while it is still being built, so the cycle is tied through `Schemas.Lazy(...)`:

```csharp
// structure TreeNode { @required label: String, child: TreeNode }
Schema<TreeNode>? node = null;
node = Schemas
    .Structure<TreeNode, Builder>(ShapeId.Parse("example#TreeNode"))
    .Required(
        "label",
        static value => value.Label,
        static (builder, value) => builder.Label = value,
        Schemas.NullableReference(Schemas.String))
    .Optional(
        "child",
        static value => value.Child,
        static (builder, value) => builder.Child = value,
        Schemas.NullableReference(Schemas.Lazy(() => node!)))
    .Build(
        static () => new Builder(),
        static builder => new TreeNode(builder.Label!, builder.Child));
```

`LazySchema<T>` resolves once and caches. It is invisible to consumers: it
forwards `Id`, `Kind`, `Traits`, and `Resolved` to its target, and its `Accept`
delegates to the target's `Accept`, so the visitor is always called by the
sealed leaf that knows its own type arguments. There is no `VisitLazy` case.

A fold cannot walk a cyclic graph eagerly, so a consumer either defers each
member body until first use or memoizes what it has already compiled. The
validator does the first. Codec plans and protocol bindings do the second,
through `SchemaCompilationCache`, which registers a placeholder for a shape
before compiling it so a self-reference resolves to the in-progress plan. The
cache keys on `Schema.Resolved`, because two references to the same shape are
different objects when one of them is a lazy stand-in.

### Projections

A projection narrows a structure's visible members while keeping the same
container type. REST protocols use one for the body: the members bound to
labels, headers, and query parameters are excluded, and the body codec is
compiled for the rest.

```csharp
var bodyProjection = Schemas.Project(inputSchema, bodyMemberNames);
```

A projection evaluates its selection once at construction and snapshots the
matching member schemas, so a codec compiled for it sees a stable view even when
the selection came from a mutable set.

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

- `required` is member metadata (`IMemberSchema.IsRequired`).
- Modeled defaults are member traits, resolved once at fold time into a typed
  factory (`DefaultValues.TryCompile`).
- `@sparse` is metadata on the list or map schema, declaring that the
  collection holds nullable elements or values.

A member's target is wrapped to carry nullability: `Schemas.NullableReference`
for a reference type and `Schemas.Nullable` for a value type. Reference-type
nullability is only an annotation in the `Schema<T>` type argument. Value-type
nullability needs `NullableSchema<T>`, because C# represents `T?` as a distinct
runtime type. A fold reads required and default metadata from the member and
value-type nullability from the schema.

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
    TResult VisitEventStream<TEvent>(EventStreamSchema<TEvent> schema);
}
```

A consumer is a fold: it visits the schema graph once and compiles a typed plan,
cached per consumer and schema. A protocol compiles transport bindings, and the
validator compiles an `ISmithyValidator<T>`. Codecs are not folds; they run the
generated [shape serializers](#shape-serializers).

The hot path runs only precompiled plans: no schema dispatch, no per-value trait
lookup, no boxing. A REST operation's labels, headers, query parameters, and
status code are each a plan compiled from the member and its value codec.

A fold that handles every kind implements `ISchemaVisitor<TResult>` directly, so
a new shape kind fails to compile until the fold handles it. A fold that admits
only a few kinds derives from `PartialSchemaVisitor<TResult>`, overrides those,
and answers the rest through `VisitDefault`.

## Shape Serializers

Every aggregate schema (structure, union, list, and map) carries a generated
shape serializer. It knows the shape's CLR types and nothing about any wire
format: it hands each value to a writer, and takes each value from a reader,
through calls typed at the value's static type.

```csharp
public interface IShapeSerializer<T>
{
    void Write<TWriter>(T value, ref TWriter writer)
        where TWriter : struct, IShapeWriter;

    T Read<TReader>(ref TReader reader)
        where TReader : struct, IShapeReader;
}
```

The writer and reader are `struct` type arguments, so the JIT compiles each
serializer once per codec with every call bound directly: no interface dispatch
and no casts per value.

A structure's serializer addresses members by declaration index. `Member(index)`
returns false when the member is outside the projection being written, and
nested aggregates are written through their own serializers:

```csharp
// structure Rating { @required title: String, score: Integer, author: User }
public void Write<TWriter>(Rating value, ref TWriter writer)
    where TWriter : struct, IShapeWriter
{
    if (writer.Member(0)) writer.WriteString(value.Title);
    if (writer.Member(1))
    {
        if (value.Score is { } score) writer.WriteInt(score);
        else writer.WriteNull();
    }
    if (writer.Member(2)) writer.Write(value.Author, UserSchema.Serializer);
}

public void ReadMember<TReader>(Builder builder, int index, ref TReader reader)
    where TReader : struct, IShapeReader
{
    switch (index)
    {
        case 0: builder.Title = reader.ReadString(); break;
        case 1: builder.Score = reader.ReadInt(); break;
        case 2: builder.Author = reader.Read(UserSchema.Serializer); break;
    }
}
```

The codec drives a structure read. It walks the wire format (JSON properties,
XML elements and attributes, CBOR keys, protobuf field numbers), maps each to a
member index, and calls `ReadMember`, so wire order is irrelevant. Once the
structure ends, absent members take their modeled defaults and the builder is
finalized, which is where a missing required member fails.

A list or map serializer iterates its own collection, and a union serializer
writes its one case as a member:

```csharp
public void Write<TWriter>(SimpleList value, ref TWriter writer)
    where TWriter : struct, IShapeWriter
{
    foreach (var item in value.Values) writer.WriteString(item);
}

public SimpleList Read<TReader>(ref TReader reader)
    where TReader : struct, IShapeReader
{
    var items = new List<string>();
    while (reader.NextElement()) items.Add(reader.ReadString());
    return SimpleList.FromOwnedList(items);
}
```

`IShapeWriter` and `IShapeReader` have one method per simple shape kind
(`WriteString`, `ReadInt`, `WriteTimestamp`, and so on), `WriteNull` and
`ReadNull`, enum value methods, `Member` for structure and union members,
`NextElement` and `NextEntry` for collections, and `Write`/`Read` for a nested
aggregate through its serializer.

## Codec Model

A codec implements `IShapeWriter` and `IShapeReader` for one wire format, plus
a *plan*: per-shape tables of wire metadata indexed by member position, built
once from the schema graph. A JSON plan holds each member's pre-encoded property
name (honoring `@jsonName`), timestamp format, and default; an XML plan holds
element and attribute names, flattening, and namespaces; a protobuf plan holds
field numbers and packing. Each table links to the tables of its members'
targets, so writing a nested value switches tables without a lookup, and a
recursive shape links back to its own table.

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
    ICodec<T> FromMember<T>(
        ITypedTargetMemberSchema<T> member,
        CodecFactoryOptions? options = null
    );
}

public interface IProjectionCodecFactory : ICodecFactory
{
    IProjectionCodec<T, TBuilder> FromProjection<T, TBuilder>(
        StructProjection<T, TBuilder> projection,
        CodecFactoryOptions? options = null
    );
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
A projection is a member mask over the structure's table, which `Member(index)`
consults. `FromMember` retains traits declared on the member, for a payload
member whose traits control its target's wire representation (`@xmlName`,
`@timestampFormat`).

A projection codec reads into a builder rather than producing a value. A
protocol creates one builder per request, lets the body codec and each HTTP
binding reader set the members they own, and finalizes the builder once.

Codecs handle body-format concerns: names, timestamp formats, enum values,
sparse collections, documents, and protobuf field numbers. They do not build
HTTP requests, expand URI labels, choose headers, or map status codes; those
belong to protocols. A format whose layout does not fit member-at-a-time
writing, such as protobuf inlining a union's cases into the enclosing message,
resolves it in its plan rather than in generated code.

## Validation

The server enforces the constraint traits: `@required`, `@length`, `@range`,
`@pattern`, `@uniqueItems`, and enum membership. Validation is a fold like any
other: it compiles a schema into an `ISmithyValidator<T>` that walks a
deserialized value and collects every violation.

```csharp
ISmithyValidator<T>? validator = SmithyValidator.FromSchema(inputSchema);
```

The fold resolves each constraint through member precedence
(`member.GetTrait(LengthTraitId)`) and compiles one checker per constrained
node. A subgraph with no reachable constraints compiles to nothing, and a schema
with no constraints at all yields no validator, so unconstrained operations pay
nothing per request.

A validator reports all violations in one pass. Each violation carries a JSON
Pointer into the value (`/tags/2`, `/attributes/color`) and a message, matching
the path `smithy.framework#ValidationExceptionField` documents, so a caller can
correct every problem from a single response.

Generated enum types stay open, so an unrecognized value deserializes rather
than throwing and a client is not broken by a server that added a member. The
server is where that openness stops: enum membership is checked from the values
the schema carries.

`@uniqueItems` compares elements through equality derived from the schema, not
from .NET: a blob is a `byte[]` and a generated structure holding a list compares
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

### REST binding layer

`NSmithy.Protocols.Rest` holds the REST wire format, shared by restJson1 and
restXml, which differ only in the `IRestBodyCodecFactory` they supply:

- `RestOperationBinding<TInput, TOutput, TInputBuilder, TOutputBuilder>`
  precomputes everything the operation schema determines: HTTP method, URI
  template, the label, header, query, and payload members, and the input and
  output body projections.
- `RestOperationProtocol<TInput, TOutput, TInputBuilder, TOutputBuilder>` is the
  per-operation protocol. It holds the binding and compiled error handling and
  delegates to `RestProtocol`, the stateless engine for URI templating, HTTP
  binding values, payloads, and error parsing.

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

Each binding compiles a typed value reader or writer in `RestProtocol` from the
member's schema.

## Alternatives Considered

### Reflection-based serialization

Scanning properties at runtime via reflection is common in .NET serializers.
Rejected: reflection loses Smithy member metadata unless the model carries
serializer-specific attributes, multiple protocols would need competing
attributes or converters on the same type, and it rules out the precomputation
that keeps the hot path free of boxing and lookups.

### Format-specific generated serializers

The generator could emit a JSON, CBOR, or XML serializer per shape, as
`System.Text.Json` source generation does. Rejected: it couples generated code
to specific wire formats, adding a codec would require regenerating every
model, and protocol projections would still need to redirect member writes.

What is generated per shape is format-agnostic: a shape serializer moves
values through typed writer and reader calls and knows nothing about any wire
format. It lives on the schema class rather than the model type, and every
codec uses the same one.

### Codecs as schema folds

A codec could fold the schema graph through the visitor into a tree of typed
reader and writer objects, as protocols and the validator do. Rejected: every
codec then reimplements the same walk over structures, unions, lists, and maps,
each with typed node classes for every shape kind, and a structure's per-member
plans can only be stored as `object` and cast back on each value. Generated
serializers make the walk a single generated method per shape and leave each
codec only its format.

### Protocol locations in core schema metadata

Core member metadata could store a normalized location such as `Body`, `Header`,
`Query`, or `Label`. Rejected: one Smithy model serves multiple protocols, and
location is a protocol's interpretation of traits, not a property of the shape.

### An erased object-shaped schema surface

Schemas could expose `object`-typed accessors and builders so heterogeneous
infrastructure can read and construct values without knowing the CLR type.
Rejected: a codec built on that surface boxes and casts on the hot path, and
keeping it alongside the typed surface means two behaviors to keep identical.
Heterogeneous code holds the non-generic `Schema` and re-enters typed code
through `Accept`.

### Trait overlays instead of members

List and map elements could be modeled as plain target schemas, with
member-level traits merged onto a wrapping schema at construction. Rejected in
favor of first-class members with a resolution helper, which keeps precedence in
one place while also representing trait origin, map-key traits, and the member
itself as a node. An overlay erases those.

### A materialized merged trait view

Members could carry a third trait collection: the member-over-target merge,
computed at construction. Rejected: trait resolution already runs once per
consumer and schema, so caching the merge buys nothing on the hot path, and the
merge cannot represent origin. A member re-declaring a trait with the target's
value is indistinguishable from declaring nothing.

### Presence as nullable wrapper schemas

Optionality could be expressed entirely by wrapping target schemas in nullable
adapters. Rejected: presence is positional in Smithy, so required, optional, and
defaulted all belong to the member. Nullable wrappers carry only the CLR
nullability of the member's value.

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
