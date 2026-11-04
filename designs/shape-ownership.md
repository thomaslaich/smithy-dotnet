# Shape Ownership

How each project generates the shapes in its own model files and references
the shapes that other projects generated.

## Overview

> **A project generates the shapes in its own model files and references the
> shapes in its references' model files. `SmithyService` adds the service.**

It is the rule C# itself follows: a project compiles its own files and
references the others. Concretely:

- **Every project with model files generates their shapes**, operations
  included: types, schemas, each operation's schema, and its handler interface.
- **Shapes from referenced projects and packages are not generated again.** The
  project references their types through the *shape manifest* their build
  wrote.
- **`SmithyService` adds the service on top:** the service schema, the operation
  table, `Add{Service}Service`, `Map{Service}Service`, and the client.

Every shape therefore has one set of .NET types, whichever projects use it.
That serves three layouts:

- **Modules of one server.** Each module owns its operations and their handler
  implementations; one project ties them into a service.
- **A client and a server of one contract.** Both reference the contract's
  types.
- **Shapes shared between services.** Common errors, pagination, or money types
  are generated once in a package, and every service that uses them shares one
  set of .NET types.

## Goals

- Every shape is generated exactly once in a graph of projects and packages;
  everything else references it.
- What a project generates follows from its files and two properties, not from
  what its model contains.
- A module that implements operations depends on `NSmithy.Core`, not on
  ASP.NET Core or protocol packages.
- The service is validated as a whole (protocols, HTTP binding conflicts, auth)
  in the project that generates it.
- Works through `ProjectReference` and `PackageReference`, with no hand-written
  MSBuild items for packages.

## Non-goals

- F#. The F# generator writes one file per kind (`Types.g.fs`,
  `Schemas.g.fs`), its symbols follow other rules, and an F# project generates
  every shape it uses.
- Sharing generated types between C# and F# projects.
- Composing several services into one.

## Example

Four projects, one Smithy namespace:

```
Acme.Shop.Common/     model/common.smithy     structure Money, @error NotFound
Acme.Shop.Orders/     model/orders.smithy     operation PlaceOrder      (uses Money, NotFound)
Acme.Shop.Payments/   model/payments.smithy   operation CapturePayment  (uses Money)
Acme.Shop.Api/        model/service.smithy    @restJson1 service Shop { operations: [PlaceOrder, CapturePayment] }
```

Orders and Payments reference Common, and Api references Orders and Payments.
Only Api sets a property:

```xml
<PropertyGroup>
  <SmithyService>acme.shop#Shop</SmithyService>
</PropertyGroup>
```

| Project | Generates | References |
| --- | --- | --- |
| Common | `Money`, `NotFound` and their schemas | Nothing |
| Orders | `PlaceOrderInput`, `PlaceOrderOutput`, `PlaceOrderSchema`, `IPlaceOrderHandler` | `Money`, `NotFound` |
| Payments | `CapturePaymentInput`, `CapturePaymentOutput`, `CapturePaymentSchema`, `ICapturePaymentHandler` | `Money` |
| Api | `ShopSchema`, `IShopServiceHandler`, `ShopServiceDefinition`, `AddShopService`, `MapShopService` | Everything above |

Orders implements `IPlaceOrderHandler` and registers it. The host references
Api and the modules, and calls `AddShopService()` and `MapShopService()`. The
modules know nothing about the service, and only Api depends on ASP.NET Core and
the protocol packages.

The same rule covers the other layouts:

| Project | `SmithyService` | Generates |
| --- | --- | --- |
| Contracts project with a service in its model | Not set | Its shapes and operations, no service code |
| Implementation of those contracts, with no model files and no `NSmithy.MSBuild` | Not set | Nothing; it implements the handler interfaces |
| Server against that contracts project | Set | The server only |
| Client against that contracts project | Set | The client only |
| Client against a Maven contract JAR | Set | Everything: no project owns those shapes |
| Model-only project, with `SmithyGenerateCode=false` | Not set | Nothing |

## Design

### Owned sources

A project's own model files are the `sources` and `imports` of its project
build file, but not the sources collected from references. MSBuild passes them
as the `csharp-codegen` setting `ownedSources`, following the per-key ownership
in [build-configuration.md](build-configuration.md#plugin-settings):

```json
"csharp-codegen": {
  "ownedSources": ["/abs/path/Acme.Shop.Orders/model"]
}
```

A build outside MSBuild sets the same key in its `smithy-build.json`.

A build file that runs `csharp-codegen` in projections other than
`SmithyProjection`, for example one per projection to generate several
services, gets no `ownedSources`: each of those projections would generate
the owned shapes again. It generates what its settings select. The same holds
for a build file without a service that configures `csharp-codegen` itself.

### The generated set

The generated set starts from:

- every shape defined in `ownedSources`, and
- with a service, every shape the service reaches;

and adds every shape those target, then removes every shape a manifest
provides. A shape no project owns, such as one from a Maven contract JAR, is
therefore generated wherever it is needed. Where the `closure` setting is set,
it selects the starting shapes explicitly.

Operations in the set are generated even without a service: their operation
schema (`{Operation}Schema`) and their handler interface (`I{Operation}Handler`).
Without a service nothing else is generated, so nothing depends on ASP.NET Core
or a protocol. A `service` shape in the owned sources generates nothing unless
`SmithyService` names it.

### Handler interfaces

`I{Operation}Handler` depends only on its operation: its input, output, errors,
and streaming members. It lives in a file of its own,
`{Operation}.Handler.g.cs`, in the operation's namespace.

The project that owns an operation always generates its handler interface,
because a server built on that project cannot, so `SmithyGenerateServer` only
decides whether the project generates the server for `SmithyService`. An
operation no project owns follows `SmithyGenerateServer`, so a client generated
from a Maven contract JAR gets no handler interfaces. An operation of a service
whose surface an integration's service generator replaces gets none.

`I{Service}ServiceHandler`, which combines the operation interfaces, stays with
the service and refers to an operation from another namespace by its qualified
name.

### The shape manifest

Every `csharp-codegen` run writes `nsmithy-shapes.json` next to its generated
files. It lists each shape the run generated, with the names of the C# symbols
that represent it:

```json
{
  "version": 1,
  "generator": "smithy-csharp-codegen",
  "generatorVersion": "0.18.0",
  "shapes": {
    "acme.shop#PlaceOrder": {
      "schema": "Acme.Shop.Schemas.PlaceOrderSchema",
      "handler": "Acme.Shop.IPlaceOrderHandler"
    },
    "acme.shop#PlaceOrderInput": {
      "type": "Acme.Shop.PlaceOrderInput",
      "schema": "Acme.Shop.Schemas.PlaceOrderInputSchema"
    }
  }
}
```

The manifest records names instead of letting consumers derive them from shape
ids. A project's `baseNamespace`, its closure renames, and collision suffixes
(see [shapes.md](shapes.md#name-collisions)) then apply in every project that
uses its shapes, without the consumer knowing those settings.

A manifest lists only the shapes its run generated. The shapes a project
references from its own references are in those references' manifests.

### Consuming manifests

The `GetSmithyShapeReferences` target returns what a project needs from a
reference and everything that reference references:

- **Their manifests**, from every C# project that generated code.
- **Their model sources and build files**, from contracts projects
  (`SmithyPublish=true`): Api needs Common's model, because the operations it
  gets from Orders target `Money`. A module therefore sets `SmithyPublish`, so
  that its model reaches the projects that reference it.

Only a project that generates code itself adds the transitive model sources
and build files. A project that generates nothing, such as a test project that
references a server, is left as it is: a model it does not use must not make it
fail. Direct references are collected through `GetSmithyContractItems` and
`GetSmithyBuildFile`.

MSBuild passes the manifests as the `csharp-codegen` setting `providedShapes`,
a list of manifest paths. The plugin then:

1. Loads the manifests and removes every shape they list from the generated
   set.
2. Resolves a provided shape to the symbols in its manifest.
3. Generates the rest as usual.

Generated code refers to other shapes by fully qualified name, through public
types and the public static `Schema` property of their schema classes. Code that
refers to a provided shape is therefore the same as code that refers to a shape
of its own.

For a service, `ServerGenerator` writes the operation table, the combined
handler interface, and the registration and mapping extensions with the
manifest's names for provided operations. The operations' JSON Schemas for
adapters such as MCP stay private to the service definition and are generated
from the full model. The client generator works the same way.

### Shape names across modules

A service's shape names are unique across namespaces, and a shape's id is unique
in the model. Modules that share a Smithy namespace therefore coordinate all
their shape names. Modules in their own Smithy namespaces can define shapes with
the same name for different concepts, and the service separates them with its
`rename` property. A concept the modules share belongs in a common project.

C# types keep the name from the model. The owning project compiled them without
knowing the service, a shape can be part of several services, and each type
stays in its own namespace.

A service cannot rename a provided error. Smithy rejects a renamed error for the
AWS protocols, and for other protocols NSmithy does not apply a rename to the
error names it sends and matches. Such errors have distinct names in their own
models, or the modules share one from a common project. Operation names are
unique within a service, because Smithy does not allow renaming operations.

### Service errors

Operation schemas are service-agnostic: they list the errors their operation
declares, in the project that owns it. A service's `errors` are on its
`ServiceSchema`. A protocol is bound to a service, so it combines the two when it
binds an operation, with `ServiceSchema.ErrorsOf`. Every operation of a service
gets its errors the same way, whichever project generated it.

For modules to throw a service's error, it is defined in a project they
reference.

### Packages

A package of a project that generates shapes ships compiled code, not C#
source: its assembly contains the generated types, as any library's does.
`dotnet pack`, with `SmithyPublish=true`, puts three things in the package:

- the assembly with the generated types;
- the model sources and `smithy-build.json` under `build/smithy/`;
- the manifest at `build/smithy/nsmithy-shapes.json`.

It also packs `buildTransitive/<PackageId>.props`, a fixed file from
`NSmithy.MSBuild` (`tools/NSmithy.ContractsPackage.props`). NuGet imports it
into every project that references the package, directly or transitively. It
declares the package's model sources, build file, and manifest as
`SmithyPackageSource`, `SmithyPackageBuildFile`, and
`SmithyPackageShapeManifest` items, which MSBuild adds to `SmithySource`,
`SmithyContractsBuildFile`, and `SmithyShapeManifest` only in projects that
generate code, under the same rule as transitive project references. An
application that references a client library gets the props too, and must not
start generating, or fail, because of them. A `PackageReference` is all a
consumer needs, and model files a project adds by hand are not added twice. The
props always bring the manifest with the model, so a consumer cannot get one
without the other.

Consumers use the package's types as the package generated them: its
`baseNamespace` and other type-shaping settings, and its NSmithy version (see
[Tradeoffs](#tradeoffs)). Settings that only affect the consumer's own output,
such as generating a client or fakes for its service, still apply. A package
that leaves generation to its consumers sets `SmithyGenerateCode=false` and
ships the model only.

The Maven contracts JAR contains the model only, and other languages generate
their own types from it.

### Opting out

`SmithyGenerateCode=false` turns C# generation off for a project without
`SmithyService`. Such a project only validates and packages its model: for
example one whose contract is published only as a Maven JAR, or whose consumers
are F# projects. Its consumers generate its shapes. Setting it together with
`SmithyService` fails the build.

## Rules

| Condition | Result |
| --- | --- |
| A project generates shapes and does not reference `NSmithy.Core` | The build fails (`NSMITHYRUNTIME`) and names the package to add. |
| `SmithyGenerateCode=false` is set together with `SmithyService` | The build fails (`NSMITHYCODE`). |
| Two manifests list the same shape | Code generation fails and names both manifests. |
| The service's `rename` renames a provided error | Code generation fails. NSmithy does not apply a rename to the error names it sends and matches. |
| A manifest's `generatorVersion` differs from the running plugin's | Code generation fails. Generated code from different versions may not fit together. |

## Alternatives Considered

### Generate everything once

One project generates the whole service, and every other project references
it. Modules that implement operations then depend on the API project and the
server's packages, a client and a server of one contract cannot share types, and
services cannot share shapes without duplicating their types.

### An explicit property per project

A property such as `SmithyShapeLibrary=true` could opt a project into generating
its own shapes. That adds a second way a project can behave, which every reader
of a project file has to know, and a client and a server of one contract only
share types if the contracts project opts in.

### Deciding by the model's content

A project whose model contains no service could generate its shapes, and one
whose model does could generate the service's. MSBuild does not read the model,
so the decision would be invisible in the effective build file, and adding a
`service` shape to a module would silently remove its handler interfaces.

### Derive symbols instead of a manifest

The consumer could compute each provided shape's names from its shape id, as
the symbol provider does for its own shapes, and only needs to know which
shapes to skip. That breaks as soon as the owner and the consumer differ in
`baseNamespace` or closure renames, or a collision suffix applies in one of
them. It also gives the plugin no record of which project owns a shape.

### Ownership by Smithy namespace

Each Smithy namespace could be generated by exactly one project, a convention
some Smithy toolchains use. It forces a namespace per module, while modules
that implement one service may share its namespace.

### One service per module

Each module could define its own Smithy service. That needs no codegen support,
but the API clients see then mirrors the server's internal structure, and a
client that uses several modules needs several clients.

### Packages with generated C# source

A package could ship its generated `.g.cs` files for consumers to compile,
instead of an assembly. Every consumer then compiles its own copy of the
package's types: two projects that reference the package produce the same
types twice, and a third project that sees both gets a type conflict (CS0433).
It would also make a package behave differently from a project reference, which
provides an assembly. Readable generated code while debugging comes from
embedding the generated files in the package's symbols (`EmbedAllSources`,
Source Link) instead.

### Runtime discovery of provided shapes

The consuming project could find provided schemas through a registry at
startup instead of at compile time. Rejected for the same reasons as
[registry-based schema discovery](serialization.md#registry-based-schema-discovery):
it fails at runtime instead of compile time and interacts badly with trimming.

## Tradeoffs

- **The service is validated later.** A module compiles with an operation that
  the service project may then reject, for example for a conflicting HTTP
  route. The service project is where the API is defined, so that is where its
  rules are checked.
- **One Smithy CLI run per project with model files.** Each generates in its
  own build. Incremental builds skip projects whose model did not change.
- **Packages pin consumers to their NSmithy version.** Generated code in a
  package was built with one NSmithy version, and the version rule makes every
  consumer use it. Inside one solution that costs nothing; a package shared
  across teams has to be updated together with NSmithy. Relaxing the rule to
  compatible versions needs a compatibility guarantee for generated code, which
  NSmithy does not give before 1.0.
- **Consumers cannot reshape a package's types.** A consumer that wants the
  package's shapes in its own namespace, or with other type-level settings, has
  to use a model-only package.
- **A shape no project owns can still be generated twice.** Two modules that
  both target a shape from a Maven JAR each generate it. The conflict is
  reported as soon as a third project sees both manifests, and the fix is a
  project that owns the shape.
- **Modules coordinate error and operation names.** A service cannot rename a
  provided error or any operation, so these names are unique across the modules
  of a service.
- **Transitive model collection.** A project that generates code gets the models
  of the contracts its references reference. Together with manifests, nothing is
  generated twice.
- **Design-time builds.** A reference that has not been built yet has no
  manifest, so an IDE build that skips building references generates the
  reference's shapes in the consumer too until the reference is built. A full
  build always builds references first.

## Open questions

- **Renames on the wire.** For protocols where Smithy allows renaming errors,
  applying a service's `rename` to the error names NSmithy sends and matches
  would let modules in their own namespaces reuse error names.
- **Ignoring a manifest.** Should a consumer be able to ignore a package's
  manifest and generate the package's shapes itself, for example in its own
  namespace? It would need the package's types to stay out of its compilation.
- **Projections.** Should a project generate a projection of its own model, for
  example without shapes tagged `internal`, and what does its manifest list
  then?
- **Manifests as assembly metadata.** Embedding the manifest in the assembly
  would remove the file from packages, but needs the assembly before code
  generation runs.
- **F#.** Whether F# projects should follow the same rule, and whether C# and
  F# projects should ever share generated types.

## Related Docs

- [codegen-architecture.md](codegen-architecture.md): generated set, closures,
  and the symbol provider
- [build-configuration.md](build-configuration.md): plugin settings and
  contracts collected from references
- [server-architecture.md](server-architecture.md): the operation table and
  handler interfaces
- [shapes.md](shapes.md): namespace mapping and name collisions
