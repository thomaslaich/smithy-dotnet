---
title: OpenAPI and controllers
description: How NSmithy compares with code-first ASP.NET Core APIs and with OpenAPI-first generation, and when each is the better fit.
---

Most .NET HTTP APIs are written code first: controllers or minimal APIs define
the endpoints, [Microsoft.AspNetCore.OpenApi](https://learn.microsoft.com/aspnet/core/fundamentals/openapi/overview)
describes them as an OpenAPI document, and tools such as
[Kiota](https://learn.microsoft.com/openapi/kiota/overview) or
[NSwag](https://github.com/RicoSuter/NSwag) generate clients from that document.
The other common approach writes the OpenAPI document by hand and generates
code from it. NSmithy is a third option: a Smithy model generates the client,
the server surface, and the validation rules.

The differences are where the contract lives, what checks the server against
it, and which wire protocols it can describe.

## Code first

In a code-first API the implementation is the contract. The OpenAPI document is
derived from it, either at runtime or at build time, where the build starts the
application to ask it for the document. A change to a controller signature,
a route, or a serializer setting changes the contract, whether or not anyone
meant it to. Reviewers see the break only if the generated document is checked
in and diffed.

The contract also exists only once the server does. Client teams wait for the
implementation, or work from a document that nothing keeps in sync with the
code that is later written.

What the document says and what the server does can drift apart. The document
describes what the generator infers from attributes and types, while routing,
model binding, and serialization are configured separately. Clients generated
from the document inherit any gap between the two.

## OpenAPI first

Writing the OpenAPI document first makes it the contract, which teams can
review and version before implementation. Generators cover both sides: Kiota
and NSwag generate clients, and NSwag and
[OpenAPI Generator](https://openapi-generator.tech/docs/generators/aspnetcore/)
generate ASP.NET Core controllers.

The generated controllers are scaffolding for ASP.NET Core's own routing,
binding, and serialization, so whether the running server matches the document
still depends on how that pipeline is configured. Server and client also come
from different generators, each with its own reading of the specification.

OpenAPI describes HTTP. An organization that also runs gRPC services keeps a
second contract format for those.

## Model first with NSmithy

A Smithy model is the contract, and the generated server implements it rather
than being described by it. Generated [handler interfaces](/smithy-dotnet/servers/)
take modeled inputs and return modeled outputs. The protocol runtime does the
routing, serialization, and [validation](/smithy-dotnet/servers/validation/) the
model specifies, and [Protocol status](/smithy-dotnet/protocols/status/) reports
its results against Smithy's protocol conformance suites. Clients come from the
same model and the same runtime.

The same model can serve [several protocols](/smithy-dotnet/protocols/overview/),
including gRPC, from one handler set. When OpenAPI tooling is needed, Smithy
[generates an OpenAPI document](/smithy-dotnet/guides/endpoint-documentation/)
from the model. [Smithy Diff](https://smithy.io/2.0/guides/evolving-models.html#using-smithy-diff)
checks compatibility between model versions in review or CI.

## Where code first or OpenAPI is the better fit

- **One API, one consumer.** An API used only by a frontend in the same
  repository gains little from a separate contract. Controllers or minimal APIs
  are less ceremony.
- **The OpenAPI document is the deliverable.** If partners consume the
  document directly and its exact shape matters, authoring OpenAPI keeps full
  control over it. Smithy's OpenAPI output is a translation of the model.
- **An existing API.** Moving a large controller-based API to a model means
  modeling every endpoint as it behaves today.
- **Build requirements.** Generation runs the Smithy CLI, a Java application,
  during `dotnet build`. The optional `NSmithy.SmithyCli` package
  [provides it with its Java runtime](/smithy-dotnet/reference/msbuild/#toolchain),
  so nothing is installed separately, but it adds to build time.
- **Maturity.** NSmithy is in preview.
  [Known limitations](/smithy-dotnet/reference/known-limitations/) lists what is
  missing.

## Choosing

Write controllers or minimal APIs when the API serves a single consumer and the
implementation may define the contract. Write OpenAPI first when the HTTP
document is the product. Choose NSmithy when several teams or languages share
the contract, when the server must implement it rather than be described by it,
or when one model has to serve more than one protocol.
