---
title: Smithy and TypeSpec
description: How the two IDLs differ, and when each is the better fit for a .NET service.
---

[TypeSpec](https://typespec.io/) is the IDL most .NET teams meet first. Microsoft
created it to author Azure's REST API specifications, and OpenAPI is still its
primary output. Microsoft's newer client emitters, including the C# one, read
the compiled TypeSpec program directly instead of going through an OpenAPI
document. Smithy came from the other direction. AWS created it to generate its
SDKs, so the model is designed to be consumed by code generators rather than
translated into another description format.

Both can describe the same HTTP API. The differences that matter for a .NET team
are where the model lives, how a protocol attaches to it, and what the toolchain
needs on the build machine.

## The model is data

A Smithy model has a specified
[JSON AST](https://smithy.io/2.0/spec/json-ast.html). Traits, including custom
ones, are declared in the model as shapes, and their values are stored in that
AST alongside everything else. Any tool in any language can read it. NSmithy's
generator is a Java plugin that reads the AST, and the C# it emits carries trait
values into runtime schemas. A custom trait such as `@owner("catalog-team")`
therefore reaches the running application without a generator change.
Preserving a trait and interpreting it are separate jobs: a new serialization
rule still needs code that understands it.

A TypeSpec program is the in-memory type graph of the TypeScript compiler.
Decorators are JavaScript functions, or
[auto decorators](https://typespec.io/docs/extending-typespec/create-decorators/#auto-decorators) that
store their arguments in the compiler's state, and emitters read that state
through the compiler's JavaScript API while the compiler runs. Nothing outside
that process sees the model unless an emitter writes it out, usually as OpenAPI.
Organization-wide conventions are consequently npm libraries: decorators,
linter rules, and emitters written in TypeScript.

This decides where an organization's conventions live. In Smithy, the custom
trait, the [validator](https://smithy.io/2.0/guides/model-linters.html) that
enforces it, and the generators that act on it all read the same model data,
and selector-based validators are configured in `smithy-build.json` rather than
written in code.

## Protocol is a binding

[Modeling contracts](/smithy-dotnet/concepts/modeling/) shows the pattern:
shapes and traits declare the contract, and a separate set of traits binds it to
a protocol.

```smithy
@error("client")
structure NoSuchResource {
    @required
    resourceType: String
}

apply Weather @alloy#simpleRestJson
apply NoSuchResource @httpError(404)
```

Replace the two `apply` lines with `apply Weather @smithy.protocols#rpcv2Cbor`
and the same operations, types, and errors travel as CBOR RPC instead. A service
can declare both protocols at once, and NSmithy serves them from
[one handler set](/smithy-dotnet/servers/hosting/). Smithy's standard protocols
also ship conformance test suites, and
[Protocol status](/smithy-dotnet/protocols/status/) reports NSmithy's results
against them.

In TypeSpec, HTTP is the standard library rather than one binding among several.
Routes, status codes, and header bindings are decorators on the models
themselves:

```typespec
@error
model NoSuchResource {
  @statusCode statusCode: 404;
  resourceType: string;
}
```

That is the right design for a language whose primary output is OpenAPI, where
the document is the HTTP contract. It also means a TypeSpec model is an HTTP
model. The [Protobuf emitter](https://typespec.io/docs/emitters/protobuf/reference/)
reads its own decorators, so a service written for HTTP does not become a gRPC
service by changing one service-level annotation.
[Augment decorators](https://typespec.io/docs/language-basics/decorators/#augmenting-decorators)
move annotations out of the model declarations, which keeps files tidy but does
not change what the model describes.

## The toolchain

`NSmithy.MSBuild` ships the generation plugins and restores the Smithy CLI with
a Java runtime for the build platform. Adding the package is the whole setup, and
generation runs inside `dotnet build`.
[Code generation](/smithy-dotnet/concepts/code-generation/) describes the bundle.
Models are shared as Maven JARs, which every Smithy toolchain can consume; see
[Distributing contracts](/smithy-dotnet/guides/distributing-contracts/).

TypeSpec runs on Node.js. The compiler and every library and emitter are npm
packages, so a .NET project that uses TypeSpec carries a `package.json` next to
its `.csproj` and needs Node on every build machine.
[Microsoft.TypeSpec.MSBuild](https://www.nuget.org/packages/Microsoft.TypeSpec.MSBuild)
wires the compiler into the build but expects it to be installed through npm.
Models are shared as npm packages.

The C# emitters are at different stages of maturity. As of September 2026,
[`@typespec/http-client-csharp`](https://www.npmjs.com/package/@typespec/http-client-csharp)
generates the Azure SDK for .NET and the OpenAI .NET library and is published as
prerelease builds;
[`@typespec/http-server-csharp`](https://www.npmjs.com/package/@typespec/http-server-csharp)
is alpha. Both target HTTP. NSmithy is itself in preview, and
[Protocol status](/smithy-dotnet/protocols/status/) lists what it covers.

## Where TypeSpec is the better fit

- **OpenAPI is the deliverable.** The OpenAPI emitter is TypeSpec's most mature
  output, and the syntax is designed to make large OpenAPI documents short to
  write. Smithy can
  [emit OpenAPI](https://smithy.io/2.0/guides/model-translations/converting-to-openapi.html)
  too, but as a translation of a model that was not designed around it.
- **Azure.** The Azure libraries add resource providers, long-running
  operations, and the conventions Azure services follow.
- **Authoring abstractions.** [Templates](https://typespec.io/docs/language-basics/templates/),
  spread, inheritance, and unions express repeated patterns compactly. Smithy's
  [mixins](https://smithy.io/2.0/spec/mixins.html) reuse members and traits but
  offer no generics or subtyping.
- **Several API versions in one model.** The
  [versioning library](https://typespec.io/docs/libraries/versioning/reference/)
  describes multiple versions of a service in one place and emits each of them.
  Smithy versions a model as a whole and checks compatibility between versions
  with [Smithy Diff](https://smithy.io/2.0/guides/evolving-models.html#using-smithy-diff).

## Choosing

Choose Smithy when the model is the product: one contract that generates clients
and servers for several protocols, carries your own traits through to runtime,
and is shared with teams working in other languages through Maven. Choose
TypeSpec when an OpenAPI document is the product, when you are building on
Azure, or when an existing Node and OpenAPI toolchain is the starting point.
