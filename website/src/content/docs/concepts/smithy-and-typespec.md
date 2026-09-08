---
title: Smithy and TypeSpec
description: Why we chose Smithy for NSmithy's model, code generation, and .NET integration.
---

Smithy and [TypeSpec](https://typespec.io/) both let teams define APIs before
implementing them. We chose Smithy for its declarative service model and the
separation between that model, code generation, and protocol behavior.

This is an architectural preference. TypeSpec can describe the same API and
provides richer authoring abstractions. The comparison below explains why
Smithy's design suits NSmithy, and where NSmithy's own implementation contributes
to that choice.

## A declarative service model

Smithy represents an API as shapes with traits. Services, resources, operations,
and data types have explicit roles in the
[semantic model](https://smithy.io/2.0/spec/model.html). Traits are identified,
structured values attached to those shapes; their definitions use Smithy shapes
too. The [JSON AST](https://smithy.io/2.0/spec/json-ast.html) gives this model,
including its traits, a portable representation.

This matters for code generation. NSmithy generates C# types and runtime schemas
that retain trait metadata. Adding a defined custom trait such as
`@owner("catalog-team")` does not require a generator change to preserve it.
A runtime component can read that metadata later. The schema generator therefore
does not need to know every extension its consumers use.

A trait's representation and its interpretation are separate responsibilities.
Preserving `@owner` is enough for a metadata consumer; implementing a new
serialization rule requires a component that understands it. Traits that affect
the C# type itself, such as `@required`, still need generator support.

TypeSpec [decorators](https://typespec.io/docs/extending-typespec/create-decorators/)
can execute code and store metadata that emitters access through library APIs.
Auto decorators also provide metadata storage without handwritten JavaScript.
Emitters can ignore annotations they do not use. Smithy's advantage for NSmithy
is the uniform representation of traits as model data: generation, validation,
and runtime interpretation can all work from it.

## Separating the contract from its protocol

Consider a library service. Smithy's
[resource model](https://smithy.io/2.0/spec/service-types.html) gives a book an
identity, properties, and lifecycle operations before assigning HTTP routes:

```smithy
$version: "2"
namespace example.library

service Library {
    version: "1"
    resources: [Book]
}

resource Book {
    identifiers: { id: String }
    properties: { title: String }
    read: GetBook
}

@readonly
operation GetBook {
    input := for Book {
        @required
        $id
    }
    output := for Book {
        @required
        $id
        @required
        $title
    }
    errors: [BookNotFound]
}

@error("client")
structure BookNotFound {
    message: String
}
```

`for Book` lets the input and output reuse the resource's definitions through
`$id` and `$title`. Smithy names the resulting shapes `GetBookInput` and
`GetBookOutput` and validates the read operation's identifier binding. Resources
can also express containment and other lifecycle operations. These are core
service concepts, available to tools independently of a REST convention.

TypeSpec expresses the lookup through its
[REST library](https://typespec.io/docs/libraries/rest/reference/):

```typespec
import "@typespec/http";
import "@typespec/rest";
using TypeSpec.Http;
using TypeSpec.Rest;

@service
namespace Library;

@resource("books")
model Book {
  @key id: string;
  title: string;
}

@error
model BookNotFound {
  @statusCode statusCode: 404;
  message: string;
}

@autoRoute
interface Books {
  @readsResource(Book)
  getBook(...Resource.KeysOf<Book>): Book | BookNotFound;
}
```

`@resource` names the collection, `@key` identifies its members, and
`Resource.KeysOf<Book>` supplies the operation's key parameter. Together,
`@readsResource` and `@autoRoute` derive `GET /books/{id}`.

The error shows where this example commits to HTTP:
`@statusCode statusCode: 404` makes `BookNotFound` an HTTP-specific response model.
The Smithy declaration describes a client fault. Its HTTP status is assigned
with the rest of the protocol binding:

```smithy
apply Library @alloy#simpleRestJson
apply GetBook @http(method: "GET", uri: "/books/{id}", code: 200)
apply GetBookInput$id @httpLabel
apply BookNotFound @httpError(404)
```

Alternatively, RPC v2 CBOR supplies dispatch and encoding rules through a service
trait:

```smithy
apply Library @smithy.protocols#rpcv2Cbor
```

For this operation, NSmithy generates the same input, output, modeled exception,
and handler interface under either protocol. The protocol adapter determines
routes, encoding, and error envelopes. The implementation receives
`GetBookInput` and returns `GetBookOutput`.

TypeSpec can also keep annotations outside declarations using
[augment decorators](https://typespec.io/docs/language-basics/decorators/#augmenting-decorators).
The distinction extends to the generated application: in the C# HTTP server
emitter we tested, `BookNotFound` becomes an `HttpServiceException` carrying
status 404. NSmithy's modeled exception leaves that mapping to the protocol
layer. This is the separation we want to preserve from model to application.

The TypeSpec output was checked with compiler 1.15.0, REST library 0.85.0, and
`@typespec/http-server-csharp` 0.58.0-alpha.31. This describes that emitter,
not a restriction on TypeSpec's language.

## Integration with .NET

`NSmithy.MSBuild` bundles the Smithy CLI, its Java runtime, the generation
plugins, and their transitive dependencies in the NuGet package. The package
version fixes the toolchain, and the bundled plugins resolve locally during
generation. The [code-generation guide](/smithy-dotnet/concepts/code-generation/#the-bundled-toolchain)
explains the bundle and the scope of its hermeticity.

MSBuild resolves model inputs, runs generation before C# compilation, and includes
the generated sources automatically. Tracked inputs and outputs support incremental
generation. Contract project references are collected automatically; NuGet model
packages use explicit imports. The [MSBuild reference](/smithy-dotnet/reference/msbuild/)
describes the configuration.

TypeSpec also provides an official
[Microsoft.TypeSpec.MSBuild](https://www.nuget.org/packages/Microsoft.TypeSpec.MSBuild)
package. Its 0.42.0 targets run generation before C# compilation, include generated
sources, and register `.tsp` files with `dotnet watch`. The package defaults to
ProviderHub controller and AutoRest emitters, with configurable emitter selection.
It locates the compiler in the project's `node_modules/.bin` directory by default.
The NuGet package contains the MSBuild targets and task assemblies, not Node.js,
the TypeSpec compiler, or emitters. Its setup instructions require installing
the compiler and libraries through npm, with Node.js available separately.

Build integration itself is therefore available in both. NSmithy's distinction
is packaging the compiler, Java runtime, plugins, and their dependencies together
with the integration. Restoring the NuGet package supplies the bundled generation
toolchain; the TypeSpec package's documented setup connects MSBuild to a separately
installed Node.js and npm toolchain. TypeSpec also offers an experimental
[standalone CLI](https://typespec.io/docs/), but it is not included in this
MSBuild package.

Smithy's behavioral vocabulary also carries into the generated clients:

| Trait | NSmithy client behavior |
| --- | --- |
| `@paginated` | Lazy page iteration, plus item iteration when an items list is modeled. |
| `@idempotencyToken` | Automatic filling of an omitted optional token. |
| `@retryable` | Error classification for the configured standard retry strategy, including throttling. |

These traits describe how clients use an operation, beyond its wire format.
Pagination calls use the same authentication and retry pipeline as ordinary
calls. See [Pagination](/smithy-dotnet/guides/client-configuration/pagination/)
and [Retry](/smithy-dotnet/guides/client-configuration/retry/).

TypeSpec also defines [pagination metadata](https://typespec.io/docs/standard-library/pagination/),
and its Azure libraries model
[long-running operations](https://azure.github.io/typespec-azure/docs/howtos/azure-core/long-running-operations/).
Our reason for choosing Smithy is its common behavioral vocabulary and its fit
with NSmithy's runtime. This does not establish broader client feature coverage
than every TypeSpec emitter.

## Where TypeSpec has advantages

TypeSpec's [templates](https://typespec.io/docs/language-basics/templates/),
property spreading, and [inheritance](https://typespec.io/docs/language-basics/models/)
can express reusable API patterns compactly. Smithy's
[mixins](https://smithy.io/2.0/spec/mixins.html) reuse members and traits, but do
not provide generic templates or establish subtyping. TypeSpec gives authors
more ways to abstract repeated declarations.

Its emitter ecosystem also makes TypeSpec useful as an authoring language for
OpenAPI, JSON Schema, and Protobuf, as well as generated clients and servers.
That is a substantial advantage when those outputs or an existing TypeSpec
workflow are the starting point.

For NSmithy, the priority is a service model whose structure and metadata remain
available throughout generation and execution. Smithy's explicit service roles,
declarative traits, and protocol separation provide that foundation.
