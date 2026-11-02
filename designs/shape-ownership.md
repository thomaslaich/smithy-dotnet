# Shape Ownership

How each project generates the shapes in its own model files and references
the shapes that other projects generated.

> **Status: proposed.** Nothing in this document is implemented yet.

## Overview

Today a project generates every shape its service reaches, or none at all
without `SmithyService`. Two projects that generate the same shapes produce two
sets of types with the same names in the same namespaces, which conflict as
soon as one assembly sees both (CS0433). A model is therefore generated in
exactly one project, and every project that needs its types references that
one. A client and a server in the same solution each generate their own copy,
so a test project cannot reference both.

That is a consequence of generated code having no way to refer to types another
project generated. Once it has one, a simpler rule works:

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
  table, `Add{Service}Service`, `Map{Service}Service`, and the client. It no
  longer switches code generation on and off.

This serves three layouts that need a shared project today:

- **Modules of one server.** Each module owns its operations and their handler
  implementations; one project ties them into a service.
- **A client and a server of one contract.** Both reference the contract's
  types instead of generating their own.
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
- The service is still validated as a whole (protocols, HTTP binding conflicts,
  auth) in the project that generates it.
- Works through `ProjectReference` and `PackageReference`, with no hand-written
  MSBuild items for packages.
- A project with `SmithyService` and no references that provide shapes
  generates what it generates today.

## Non-goals

- F#. The F# generator writes one file per kind (`Types.g.fs`,
  `Schemas.g.fs`), and its symbols follow other rules. It keeps today's
  behavior and can follow later.
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
| Common | `Money`, `NotFound` and their schemas | — |
| Orders | `PlaceOrderInput`, `PlaceOrderOutput`, `PlaceOrderSchema`, `IPlaceOrderHandler` | `Money`, `NotFound` |
| Payments | `CapturePaymentInput`, `CapturePaymentOutput`, `CapturePaymentSchema`, `ICapturePaymentHandler` | `Money` |
| Api | `ShopSchema`, `IShopServiceHandler`, `ShopServiceDefinition`, `AddShopService`, `MapShopService` | everything above |

Orders implements `IPlaceOrderHandler` and registers it. The host references
Api and the modules, and calls `AddShopService()` and `MapShopService()` as
today. The modules know nothing about the service, and only Api depends on
ASP.NET Core and the protocol packages.

The same rule covers the other layouts:

| Project | `SmithyService` | Generates |
| --- | --- | --- |
| Contracts project with a service in its model | — | Its shapes and operations, no service code |
| Server against that contracts project | set | The server only |
| Client against that contracts project | set | The client only |
| Client against a Maven contract JAR | set | Everything, as today: no project owns those shapes |
| Model-only project | — plus `SmithyGenerateShapes=false` | Nothing, as today |

## Design

### Owned sources

A project's own model files are its `SmithySources` and the `sources` of its
project build file, but not the sources collected from references. MSBuild
passes them as the `csharp-codegen` setting `ownedSources`, following the
per-key ownership in
[build-configuration.md](build-configuration.md#plugin-settings):

```json
"csharp-codegen": {
  "ownedSources": ["/abs/path/Acme.Shop.Orders/model"]
}
```

A build outside MSBuild sets the same key in its `smithy-build.json`.

### The generated set

The generated set starts from:

- every shape defined in `ownedSources`, and
- with a service, every shape the service reaches, as today;

and adds every shape those target, then removes every shape a manifest
provides. A shape no project owns, such as one from a Maven contract JAR, is
therefore generated wherever it is needed, as it is today. Where the `closure`
setting is set, it keeps selecting the starting shapes explicitly.

Operations in the set are generated even without a service: their operation
schema (`{Operation}Schema`) and their handler interface (`I{Operation}Handler`).
Without a service nothing else is generated, so nothing depends on ASP.NET Core
or a protocol. A `service` shape in the owned sources generates nothing unless
`SmithyService` names it.

### Handler interfaces

`I{Operation}Handler` depends only on its operation: its input, output, errors,
and streaming members. It already has its own file,
`{Operation}.Handler.Server.g.cs`, next to `{Service}.Server.g.cs`, and the
`.Server` suffix keeps it inside the MSBuild globs that `SmithyGenerateServer`
controls. Today it is written with the service, in the service's namespace.
With this design it is written with the operation, so that it can be generated
without the service, in the operation's namespace. Its name stays the same.
`I{Service}ServiceHandler`, which combines the operation interfaces, stays with
the service.

### The shape manifest

Every `csharp-codegen` run writes `nsmithy-shapes.json` next to its generated
files. It lists each shape the run generated, with the names of the C# symbols
that represent it:

```json
{
  "version": 1,
  "generator": "smithy-csharp-codegen",
  "generatorVersion": "0.18.0",
  "assembly": "Acme.Shop.Orders",
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

A project receives two things from each reference that generates shapes:

- **Its model sources**, collected through `GetSmithyContractItems` as today,
  but transitively: Api needs Common's model, because the operations it gets
  from Orders target `Money`.
- **Its manifest**, collected through a new `GetSmithyShapeManifests` target,
  also transitively.

MSBuild passes the manifests as the `csharp-codegen` setting `providedShapes`,
a list of manifest paths. The plugin then:

1. Loads the manifests and removes every shape they list from the generated
   set.
2. Wraps `CSharpSymbolProvider` so that a provided shape resolves to the
   symbols in its manifest.
3. Generates the rest as usual.

Generated code already refers to other shapes by fully qualified name, through
public types and the public static `Schema` property of their schema classes.
Code that refers to a provided shape is therefore the same as code that refers
to a shape of its own, and the runtime needs no change.

For a service, `ServerGenerator` writes the operation table, the combined
handler interface, and the registration and mapping extensions with the
manifest's names for provided operations. The operations' JSON Schemas for
adapters such as MCP stay private to the service definition and are generated
from the full model, as today. The client generator works the same way.

### Packages

A package of a project that generates shapes ships compiled code, not C#
source: its assembly contains the generated types, as any library's does.
`dotnet pack`, with `SmithyPublish=true`, puts three things in the package:

- the assembly with the generated types;
- the model sources and `smithy-build.json` under `build/smithy/`, as today;
- the manifest at `build/smithy/nsmithy-shapes.json`.

It also writes `buildTransitive/<PackageId>.props`, which adds the package's
model sources, build file, and manifest as `SmithySource`,
`SmithyContractsBuildFile`, and `SmithyShapeManifest` items. NuGet imports that
file into every project that references the package, directly or
transitively, so a `PackageReference` is all a consumer needs. This replaces
the explicit items that
[Distributing contracts](../website/src/content/docs/guides/distributing-contracts.mdx)
asks for today.

Consumers use the package's types as the package generated them: its
`baseNamespace` and other type-shaping settings, and its NSmithy version (see
[Tradeoffs](#tradeoffs)). Settings that only affect the consumer's own output,
such as generating a client or fakes for its service, still apply. A package
that should leave generation to its consumers sets `SmithyGenerateShapes=false`
and ships the model only, as packages do today.

The Maven contracts JAR is unaffected. It contains the model only, and other
languages generate their own types from it.

### Opting out

`SmithyGenerateShapes=false` turns C# generation off for a project without
`SmithyService`. Such a project only validates and packages its model, as a
contracts project does today: for example one whose contract is published only
as a Maven JAR. Its consumers generate its shapes, as today. Setting it
together with `SmithyService` fails the build.

## Rules

| Condition | Result |
| --- | --- |
| A project generates shapes and does not reference `NSmithy.Core` | The build fails and names the package to add. |
| `SmithyGenerateShapes=false` is set together with `SmithyService` | The build fails. |
| Two manifests list the same shape | Code generation fails and names both assemblies. |
| A model file comes from a package that has a manifest, but the manifest is not provided | Code generation fails instead of generating the package's shapes again. |
| The service's `rename` renames a provided shape | Code generation fails. The owning project generated the shape under its own name, so the rename cannot apply. |
| A manifest's `generatorVersion` differs from the running plugin's | Code generation fails. Generated code from different versions may not fit together. |

## Migration

This is a breaking change:

- **Contracts projects without `SmithyService` generate code.** They need a
  `NSmithy.Core` reference, or `SmithyGenerateShapes=false` to keep today's
  behavior.
- **Their consumers reference the contracts project's types** instead of
  generating their own. Code that used the consumer's copies keeps compiling
  when both use the same `baseNamespace`: the types have the same names, now
  from another assembly.
- **Packaged contracts no longer need hand-written items.** Consumers that add
  `SmithySource` and `SmithyContractsBuildFile` by hand remove them; the
  package's `.props` file adds them, with the manifest.
- **Handler interfaces follow their operation's namespace.** An operation
  defined in another Smithy namespace than its service gets its handler
  interface in that namespace instead of the service's. Operations in the
  service's namespace see no change.

A project with `SmithyService` that references no project or package with a
manifest generates the same code as before, apart from the namespace of
handler interfaces for operations outside the service's namespace.

## Alternatives Considered

### Generate everything once (today)

One project generates the whole service, and every other project references
it. This works and needs nothing new, but modules that implement operations
depend on the API project and the server's packages, a client and a server of
one contract cannot share types, and services cannot share shapes without
duplicating their types.

### An explicit property per project

A property such as `SmithyShapeLibrary=true` could opt a project into
generating its own shapes, leaving every other project as it is today. It is
not a breaking change, but it adds a second way a project can behave, which
every reader of a project file has to know, and it leaves the client/server
duplication in place unless contracts projects opt in too.

### Deciding by the model's content

A project whose model contains no service could generate its shapes, and one
whose model does could keep today's behavior. MSBuild does not read the model,
so the decision would be invisible in the effective build file, and adding a
`service` shape to a module would silently remove its handler interfaces.

### Derive symbols instead of a manifest

The consumer could compute each provided shape's names from its shape id, as
the symbol provider does for its own shapes, and only needs to know which
shapes to skip. That breaks as soon as the owner and the consumer differ in
`baseNamespace` or closure renames, or a collision suffix applies in one of
them. It also gives the plugin no record of which assembly owns a shape.

### Ownership by Smithy namespace

Each Smithy namespace could be generated by exactly one project, a convention
some Smithy toolchains use. It forces a namespace per module, while modules
that implement one service usually share its namespace.

### One service per module

Each module could define its own Smithy service. That needs no new codegen,
but the API clients see then mirrors the server's internal structure, and a
client that uses several modules needs several clients.

### Packages with generated C# source

A package could ship its generated `.g.cs` files for consumers to compile,
instead of an assembly. Every consumer then compiles its own copy of the
package's types: two projects that reference the package produce the same
types twice, and a third project that sees both gets the conflict this design
removes. It would also make a package behave differently from a project
reference, which provides an assembly. Readable generated code while
debugging comes from embedding the generated files in the package's symbols
(`EmbedAllSources`, Source Link) instead.

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
- **Transitive model collection.** Today a project collects the sources of its
  direct references only. Collecting transitively changes what some existing
  projects receive; together with manifests, nothing is generated twice.

## Open questions

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
