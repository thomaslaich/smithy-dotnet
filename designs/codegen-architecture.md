# Codegen Architecture

How the NSmithy Java plugin generates C# files from Smithy models.

## Overview

NSmithy generates C# files from Smithy models using a Java Smithy plugin
(`smithy-csharp-codegen`), with AWS support in a separate integration
(`smithy-csharp-aws-codegen`). A second plugin, `smithy-fsharp-codegen`, generates
F# types and schemas for F# projects (see [F# Generator](#f-generator)). Both
plugins build on `smithy-dotnet-codegen-core`. The plugin runs inside `smithy build` via the Smithy
CLI, which means it has direct access to Smithy's fully assembled and validated
semantic model. MSBuild invokes `smithy build` before C# compilation and
registers the generated files with the .NET toolchain.

The two sides of the architecture are:

- **Java (codegen)**: model assembly, validation, trait resolution, and C#
  emission all happen inside the Java plugin during `smithy build`.
- **.NET (runtime + build integration)**: `NSmithy.MSBuild` invokes `smithy build`,
  picks up the generated files, and adds them to the `dotnet build` compilation.
  The `.NET` runtime packages (`NSmithy.Core`, `NSmithy.Protocols.*`, etc.) provide
  the protocol dispatch, schema metadata, codec interfaces, and transport
  abstractions that the generated code depends on.

## Goals

- Give consumers a `dotnet build`-first experience: no separate codegen step.
- Ship required tooling as NuGet packages: consumers should not need to install a
  Java runtime or `smithy-cli` separately, but can provide their own Smithy CLI.
- Reuse Smithy's model front end for IDL parsing, assembly, validation, and
  trait resolution — do not reimplement these in .NET.
- Keep the generated code idiomatic C#: it should look like it was written by
  hand, not produced by a template engine.
- Support multiple protocols from a single model.
- Enable incremental builds: regenerate only when model inputs change.

## Codegen Pipeline

```
smithy-build.json
      │
      ▼
smithy build (Smithy CLI)
      │   loads smithy-csharp-codegen Java plugin
      │   assembles + validates model
      │   runs CodegenDirector
      │     ├── StructureGenerator       → <Shape>.g.cs
      │     ├── UnionGenerator           → <Shape>.g.cs
      │     ├── ErrorGenerator           → <Shape>.g.cs
      │     ├── List/MapGenerator        → <Shape>.g.cs
      │     ├── Enum/IntEnumGenerator    → <Shape>.g.cs
      │     ├── OperationSchemaGenerator → <Operation>.g.cs
      │     ├── ServiceSchemaGenerator   → <Service>.Schema.g.cs
      │     ├── ClientGenerator          → <Service>.Client.g.cs
      │     └── ServerGenerator          → <Service>.Server.g.cs
      │
      ▼
  obj/Smithy/<projection>/csharp-codegen/**/*.g.cs
```

MSBuild then picks up the generated files via `NSmithy.MSBuild`:

- `_AddSmithyGeneratedCompileItems` – adds `.g.cs` files to `<Compile>`.

## Plugin Design

### Entry point

`CSharpCodegenPlugin` implements `SmithyBuildPlugin`. It is discovered on
the classpath via the standard Java `ServiceLoader` mechanism using
`META-INF/services/software.amazon.smithy.build.SmithyBuildPlugin`.

When `smithy build` runs and finds a `plugins` block referencing `csharp-codegen`
in `smithy-build.json`, Smithy invokes `CSharpCodegenPlugin.execute()`.

### Orchestration

The plugin delegates to `DirectedCSharpCodegen`, which implements
`DirectedCodegen`. Smithy's `CodegenDirector` calls a method for each shape kind
in the generated set:

| Method | Generator | Output |
| --- | --- | --- |
| `generateStructure` | `StructureGenerator` | `<Shape>.g.cs` |
| `generateUnion` | `UnionGenerator` | `<Shape>.g.cs` |
| `generateError` | `ErrorGenerator` | `<Shape>.g.cs` |
| `generateList` | `ListGenerator` | `<Shape>.g.cs` |
| `generateMap` | `MapGenerator` | `<Shape>.g.cs` |
| `generateEnumShape` | `StringEnumGenerator` | `<Shape>.g.cs` |
| `generateIntEnumShape` | `IntEnumGenerator` | `<Shape>.g.cs` |
| `generateOperation` | `OperationSchemaGenerator` | `<Operation>.g.cs` |
| `generateService` | `ServiceSchemaGenerator` | `<Service>.Schema.g.cs` |
| `generateService` | `ClientGenerator` + `ServerGenerator` | `<Service>.Client.g.cs`, `<Service>.Server.g.cs` |

All files are written under the Smithy projection's output directory:
`<SmithyBuildOutputPath>/<projection>/csharp-codegen/`.

### Generated set

The settings name a `service`, a `closure`, or both. A closure is a
`shapeClosures` metadata entry in the model, resolved by Smithy's
`ShapeClosureIndex`:

| Settings | Generated set | `generateService` |
| --- | --- | --- |
| `service` | Shapes the service reaches | Yes |
| `closure` | Shapes in the closure | No; data shapes only |
| Both | Shapes in the closure, which must contain the service | Yes |

When integrations add shapes to the service (`additionalShapes`), the plugin
gives the director a closure of its own, `nsmithy.generated`: the configured
closure, or the service when none is configured, plus the added shapes the
configured closure does not already contain. The plugin discovers integrations
before the director runs, so the closure can include their shapes, and hands
the director the same list.

### Integrations

`CSharpIntegration` extends Smithy's `SmithyIntegration`. `CodegenDirector`
finds its implementations with the Java `ServiceLoader`, through
`META-INF/services/io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpIntegration`
in any JAR on the codegen classpath, and orders them by `name()`, `runBefore()`,
and `runAfter()`. A package outside this repository extends the generator by
shipping such a JAR; the generator does not reference it.

An integration is a JAR on the codegen classpath. A project adds it to
`maven.dependencies` in its `smithy-build.json`; NSmithy merges that into the
effective build file, and the `ServiceLoader` finds the integration. A NuGet
package can add the dependency for its users through a `SmithyContractsBuildFile`
fragment, but nothing requires one.

Integrations get Smithy's hooks (`preprocessModel`, `decorateSymbolProvider`,
`interceptors`, `customize`) and the C# generator's own. Three of those shape
what a service generates:

| Hook | Use | The generator still emits |
| --- | --- | --- |
| `protocols()` | A request/response protocol: a new wire format or HTTP binding | Client, server, DI registration, endpoint resolution, shapes |
| `serviceGenerator(context, service)` | A service that does not fit request/response, such as messaging | Shapes, schemas, and operations |
| `additionalShapes(model, settings, service)` | Shapes the service does not reach through its operations, such as events it publishes | Everything, plus these shapes and their closures |

The endpoint rules hooks are described in [endpoint-rules.md](endpoint-rules.md).
Generators also expose code sections to the `interceptors` hook:
`ClientConfigDefaultsSection` is the body of the parameterless
`{Service}ClientConfig` constructor, where an integration sets a client's
defaults.

The hooks and the types they receive are `@SmithyUnstableApi`, so an
integration is built against the generator version it runs with.

#### Protocols

Generated clients and servers are protocol-agnostic: they call a runtime
`IProtocol` that serializes from schemas, so a protocol needs no generated
serializers. What code generation needs from a protocol is a `CSharpProtocol`:

```java
public record CSharpProtocol(
    ShapeId trait,          // the protocol trait, such as aws.protocols#restJson1
    String name,            // used in generated identifiers, such as RestJson1
    Symbol runtimeType,     // the IProtocol implementation
    boolean eventStreams,   // whether the runtime protocol binds event-stream operations
    boolean server) {       // whether the runtime protocol also serves requests

  /** A runtime type from its full C# name, such as "Acme.Protocols.MsgPackProtocol". */
  public static Symbol runtimeType(String fullName) { ... }
}
```

The generator registers the built-in protocols as `CSharpProtocol` values, and
each integration adds its own through `protocols()`. A service's protocols are
the registered ones whose trait it carries, in registration order: the built-in
protocols in the order below, then integration protocols in integration order.

- The client's default protocol is the `defaultProtocol` code generation setting
  when set, which must name one of the service's protocol traits, and otherwise
  the first of the service's protocols. `{Service}ClientConfig.Protocol` selects
  another at runtime. `Add{Service}Client` registers the same default.
- The HTTP version preference comes from the trait's `http` and
  `eventStreamHttp` members, the convention of Smithy's protocol traits, when
  the trait has them.
- The server exposes the service over each protocol with `server`, and `name`
  names its member of the generated protocols enum that selects which protocols
  to map. Routing belongs to the runtime protocol, not to code generation; see
  [server-architecture.md](server-architecture.md).
- The client binds event-stream operations only when one of the service's
  protocols has `eventStreams`.

The built-in protocols, in order:

| Protocol | `name` | `eventStreams` | `server` |
| --- | --- | --- | --- |
| `smithy.protocols#rpcv2Cbor` | `RpcV2Cbor` | yes | yes |
| `smithy.protocols#rpcv2Json` | `RpcV2Json` | yes | yes |
| `alloy#simpleRestJson` | `SimpleRestJson` | yes | yes |
| `aws.protocols#restJson1` | `RestJson1` | yes | yes |
| `alloy.proto#grpc` | `Grpc` | yes | yes |

The AWS integration registers the AWS protocols, in this order, all client-only
and without event streams: `aws.protocols#restXml` (`RestXml`),
`aws.protocols#awsQuery` (`AwsQuery`), `aws.protocols#ec2Query` (`Ec2Query`),
`aws.protocols#awsJson1_1` (`AwsJson1_1`), and `aws.protocols#awsJson1_0`
(`AwsJson1_0`). `aws.protocols#restJson1` is built in, since services outside
AWS use it.

Two registrations for the same trait, or two protocols of one service with the
same `name`, fail code generation. A service whose traits match no registered
protocol gets client and handler interfaces only.

An integration protocol ships its runtime type in a NuGet package that the
generated code's project references, as `NSmithy.Protocols.RpcV2Json` does for
a built-in one. A protocol whose messages need more than the model provides,
such as Thrift's numeric field ids, ships those traits in its Smithy JAR and
validates their use. Constraints a protocol places on the model are Smithy
validators too, so the generators contain no protocol-specific checks: the
built-in gRPC protocol's requirement that an event-stream input or output holds
only its event stream is the `GrpcEventStream` validator.

Protocols are HTTP protocols: generated clients send `SmithyHttpRequest`s
through an `IHttpTransport`, and generated servers are ASP.NET Core endpoints.
Each call is one request and one response. A protocol on another transport
(stdio, raw TCP, a message broker), or with exchanges that are not one request
and one response (JSON-RPC batches and notifications), is a service generator.

#### Service generators

`serviceGenerator(context, service)` replaces the generated client and server
surface of a service. It is for protocols whose services are not request and
response, such as producing and consuming messages, which neither the generated
client nor the server routing styles fit. Shapes, schemas, and operations are
still generated by the host. A service generator takes precedence over the
service's protocols. At most one integration may provide a service generator
for a service; two fail code generation.

### Symbol provider

`CSharpSymbolProvider` maps each Smithy shape to a C# `Symbol` carrying the
namespace, type name, and import list. The mapping follows the rules in
[shapes.md](shapes.md).

### Writers

`CSharpWriter` wraps Smithy's `CodeWriter` with C#-specific helpers (namespace
blocks, using statements, XML doc comments). `CSharpDelegator` manages file
creation and routes each shape's output to the correct `.g.cs` file.

## Shared Codegen Core

`smithy-dotnet-codegen-core` holds what the C# and F# generators decide the same
way, whatever language they write. It registers no plugin of its own.

| Class | What it decides |
| --- | --- |
| `ShapeSupport` | Shape predicates: required, nullable, defaults, HTTP bindings, streaming, error messages |
| `SchemaSupport` | What a runtime schema carries: runtime traits, a member's effective traits, enum values, prelude schemas |
| `Naming` | PascalCase and camelCase, and Smithy namespace to .NET namespace |
| `ShapeClosures` | The generated set: service, closure, and shapes integrations add |
| `TraitIds` | Ids of traits outside the Smithy prelude |

Rendering stays in each generator: `CSharpShapeSupport` writes C# type
expressions and literals, and the F# generator its own.

## F# Generator

`FSharpCodegenPlugin` (`fsharp-codegen`) generates the F# type and runtime
schema of every shape in the generated set. It takes `service`, `closure`, and
`baseNamespace`, with the same meaning as for `csharp-codegen`. The schemas
derive from the same runtime classes as the C# ones (`StructSchema`,
`UnionSchema`, `ListSchema`, `MapSchema`), so every codec and protocol reads and
writes F# values unchanged.

| Shape | F# type |
| --- | --- |
| structure | Record. A structure with no members is a class with value equality. |
| `@error` structure | Class deriving from `Exception`, taking optional members as optional constructor arguments |
| union | Discriminated union with an `Unknown of tag * value` case; a `Unit` member is a case without data |
| enum | Discriminated union with an `Unknown of string` case, implementing `IStringEnumValue` |
| intEnum | F# enum |
| list / set | `'T list` (`'T option list` when `@sparse`) |
| map | `Map<string, 'T>` (`Map<string, 'T option>` when `@sparse`) |

A member is an `option` when it may be absent: not `@required` and without a
default a client can rely on. A member with such a default is a plain value,
which reading fills in when the input leaves it out.

F# compiles files, and the declarations in a file, in order, and a declaration
can only refer to one before it. The plugin therefore writes two files,
`Types.g.fs` and `Schemas.g.fs`, compiled in that order. Each holds one
`namespace rec` section per Smithy namespace, so the shapes of a namespace can
refer to each other, with the sections in dependency order. Namespaces whose
shapes refer to each other fail generation.

Every reference is qualified from `global`, so a model type never hides an F#
or runtime type and the files need no `open` declarations. A schema refers to
another structure, union, list, or map through `Schemas.Lazy`, because F#
initializes a file's static values in declaration order; enum schemas come first
in their section and are referred to directly.

The runtime's serializer and deserializer type parameters carry no `allows ref
struct`: F# cannot override a method that declares it.

## MSBuild Integration

`NSmithy.MSBuild` is a NuGet package that provides the MSBuild targets used to
generate code and register it with compilation:

### `GenerateSmithyCode`

Runs before `CoreCompile`. Executes:

```
smithy build --config <smithy-build.json> --output <SmithyBuildOutputPath>
```

Incremental: the target is only re-executed when `.smithy` or `.json` model
files (or `smithy-build.json`) change, tracked via a stamp file.

### `_AddSmithyGeneratedCompileItems`

Depends on `GenerateSmithyCode`. Adds all `.g.cs` files matching
`<SmithyBuildOutputPath>*/csharp-codegen/**/*.g.cs` to `<Compile>`.
Respects `SmithyGenerateClient` / `SmithyGenerateServer` properties to
optionally exclude client or server files.

### `_AddSmithyGeneratedFSharpCompileItems`

Runs instead of `_AddSmithyGeneratedCompileItems` in an F# project, where
`SmithyPlugin` defaults to `fsharp-codegen`. Adds `Types.g.fs`, then
`Schemas.g.fs`, to `<CompileBefore>` ahead of `FSharpSourceCodeCompileOrder`, so
the project's own files come after them.

## Configuration

`smithy-build.json` controls the codegen plugin:

```json
{
  "version": "1.0",
  "sources": ["model"],
  "maven": {
    "dependencies": [
      "io.github.thomaslaich.nsmithy:smithy-csharp-codegen:<version>"
    ]
  },
  "plugins": {
    "csharp-codegen": {
      "service": "example.hello#HelloService",
      "baseNamespace": ""
    }
  }
}
```

`CSharpSettings` carries the plugin configuration read from the `plugins` node.
The `baseNamespace` prefix is prepended to Smithy namespace segments to form C#
namespaces; an empty string means the Smithy namespace is used as-is (with
segments capitalised to PascalCase).

## .NET Runtime Packages

Generated code depends on .NET packages published to NuGet:

- `NSmithy.Core` — `Schema`, `ShapeId`, `Trait`, codec interfaces
- `NSmithy.Http` — `IHttpTransport`, `SmithyHttpRequest`, `SmithyHttpClientResponse`
- `NSmithy.Client` — `SmithyClientRuntime`, `IClientInterceptor`
- `NSmithy.Server` / `NSmithy.Server.AspNetCore` — host-neutral operation
  catalogs and server framework
- `NSmithy.Server.Mcp` — MCP tools and prompts projected from generated service
  definitions
- `NSmithy.Codecs.Json/Xml/Cbor` — schema-bound body codec implementations
- `NSmithy.Protocols.Rest` — shared REST HTTP binding projection
- `NSmithy.Protocols.*` — protocol adapters such as restJson1, restXml,
  rpcv2Cbor, and gRPC

These packages are independent of the Java plugin. A consumer project references
them in its `.csproj`; the generated `.g.cs` files import the matching types.

## Generated Code Shape

Each generated shape file contains:

1. A C# record or class for the shape (see [shapes.md](shapes.md)).
2. A generated builder type when deserialization needs staged construction.
3. A separate static schema description containing the shape kind, traits,
   member schemas, typed accessors, and builder hooks at runtime (see
   [serialization.md](serialization.md)).

Operation files contain:

- `<Operation>Schema` — an `OperationSchema<TInput, TOutput>` that references
  the input and output schemas plus operation traits; it also implements the
  non-generic `IOperationSchema` view used by protocol-neutral adapters.

Service files contain:

- `<Service>.Schema.g.cs` — a `ServiceSchema` with the service shape id and
  service-level traits.
- `<Service>.Client.g.cs` — typed async methods for each operation; binds
  protocol and transport at construction time.
- `<Service>.Server.g.cs` — handler interfaces, a DI-registered
  `IServiceDefinition` holding the service's operation table, prompt
  definitions generated from `smithy.ai#prompts`, JSON Schema 2020-12
  companions for unary operations, and `Map{Service}`.

Generated client and server methods bind the service schema, operation schemas,
typed input/output values, selected protocol adapter, and transport options into
the runtime protocol pipeline. The protocol adapter projects each operation into
transport fields and delegates body payloads to a schema-bound codec such as
`JsonCodecFactory`.

The generated service definition is transport-neutral. Its operation table
pairs each operation schema with a function that resolves the handler from a
request's services, and contains no routes or protocol code. It also exposes
prompt definitions, but does not interpret or execute their templates. Every
host adapter works from the same table: ASP.NET Core binds its operations to
the selected protocols and maps the protocols' routes, and the MCP adapter
creates tools from it and renders prompts. JSON Schema metadata lives on the
server operation rather than the shared operation schema, so client-only
generation does not carry tool metadata. See
[server-architecture.md](server-architecture.md).

## Tradeoffs

**What the Java plugin approach gives us:**

- direct access to Smithy's semantic model — no reimplementing IDL parsing,
  shape assembly, or trait resolution in .NET
- protocol correctness from Smithy's own model (trait semantics are authoritative)
- interoperability with the Smithy plugin ecosystem (transforms, projections,
  external trait libraries like `alloy`)

**What it costs:**

- the generator and its tests live in a Java/Gradle build, which is a separate
  toolchain from the .NET runtime
- backend iteration (edit generator → test generated output) involves building
  the JAR and invoking `smithy build`, which is slower than an in-process loop
- packaging and release management spans NuGet (runtime) and Maven Central
  (codegen JAR)

These costs are accepted because the Java plugin gives access to Smithy's
semantic model at the layer where model semantics are most precisely defined.

## Alternatives Considered

### Pure .NET generator reading Smithy JSON AST

An earlier design explored parsing the Smithy JSON AST in .NET and running code
generation entirely inside the .NET toolchain.

**Rejected because:**

- IDL parsing, model assembly, trait validation, and projection handling would
  all need to be reimplemented in .NET — a large maintenance surface for
  semantics that Smithy already defines authoritatively.
- Any divergence from Smithy's reference implementation would silently produce
  incorrect code.
- Direct access to Smithy's `CodegenDirector` APIs gives the plugin
  first-class trait resolution and transformation without a translation layer.

### Gradle-only workflow

Codegen could be driven by Gradle rather than MSBuild, following the pattern
used by `smithy-python` and other Smithy generators.

**Rejected because:**

- .NET developers use `dotnet build`; introducing a Gradle step would require
  them to install and understand a second build system.
- `NSmithy.MSBuild` keeps the entire developer experience inside MSBuild while
  still using the Smithy CLI (and therefore the Java plugin) for generation.

## Related Docs

- [shapes.md](shapes.md) — Smithy shape → C# type mapping
- [serialization.md](serialization.md) — codec and protocol binding
- [MSBuild Reference](/smithy-dotnet/reference/msbuild/)
