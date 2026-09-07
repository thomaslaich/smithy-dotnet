---
title: Introduction
description: Design, share, and evolve API contracts with Smithy, then build their .NET implementation with NSmithy.
---

[Smithy](https://smithy.io/2.0/) is a language for defining API contracts. NSmithy
uses those contracts to generate C# clients, data types, and ASP.NET Core server
interfaces, so teams can agree on an API before implementing it.

AWS developed Smithy to address a problem at scale: many services, continually
evolving APIs, and SDKs in many programming languages. A change to a service needs
to reach each SDK consistently. A shared model gives generators a common definition
of the operations, data, errors, and behavior that each SDK must support.

## Keeping APIs consistent across teams

The same problem appears in large organizations. Teams need to find the contract
for a service, know which version they depend on, and understand whether a proposed
change will break their applications. They also need shared conventions for naming,
errors, authentication, and documentation across services built in different
languages.

When the contract is generated from server code, API design becomes tied to that
implementation. Reviewing a proposed interface means reviewing server changes, and
client developers may have to wait for that work before they can generate types or
try a mock implementation.

OpenAPI supports [design-first development](https://learn.openapis.org/best-practices.html),
but authoring and reviewing a large YAML or JSON description can be cumbersome.
It can be easier to write server code and generate the description afterward,
tying the contract to the implementation again. Smithy provides a compact language
for writing the contract itself, making it practical to discuss operations, data,
and errors before building the service.

Operations, data types (called *shapes*), and annotations (called *traits*) describe
the API in files that teams can review and version independently of server code.

TypeSpec also addresses this authoring problem. See
[Smithy and TypeSpec](/smithy-dotnet/getting-started/smithy-and-typespec/) for a
comparison of their modeling, tooling, and .NET workflows.

Smithy's tooling also supports the governance around that contract:
[validators](https://smithy.io/2.0/guides/model-linters.html) can enforce shared
modeling rules, and [Smithy Diff](https://smithy.io/2.0/guides/evolving-models.html#using-smithy-diff)
can detect backward-compatibility issues between model versions. Teams can publish
models as versioned dependencies in their chosen artifact repository. This gives
API review and compatibility checks a place in the build process.

## A contract that can span protocols

Protobuf and gRPC also let teams define interfaces before writing implementations.
But an organization may need REST APIs for external consumers, gRPC between
services, and asynchronous messages between systems. Choosing gRPC for some calls
still leaves contracts to manage for those other interfaces.

Smithy separates the service model from its wire protocol. The operations and data
can be shared while protocol traits specify how they are transmitted. A service
can declare multiple protocols, and generators can produce the corresponding
clients and servers. Smithy tooling can also
[derive OpenAPI descriptions](https://smithy.io/2.0/guides/model-translations/converting-to-openapi.html)
for HTTP APIs.

That separation is useful for asynchronous contracts too, where a shared data
model should not dictate a single message encoding. Delivery semantics and
transport integration still require suitable traits and tooling; a model alone
does not provide a messaging implementation.

## Bringing the contract to .NET

NSmithy runs code generation as part of `dotnet build`. From your `.smithy` files,
it generates the parts enabled for each project:

- **Model types** for inputs, outputs, and errors.
- **Async clients** with typed methods for calling service operations.
- **Server handler interfaces and ASP.NET Core routing** for implementing them.
- **Optional fake clients and handlers** for trying the contract before the service
  implementation is ready, using modeled examples or generated placeholder values.

You write the handler behavior; NSmithy handles request and response serialization.
Clients and servers can live in separate projects and share the model as a
[versioned contract](/smithy-dotnet/guides/distributing-contracts/).

NSmithy supports REST/JSON, RPC v2 CBOR, and gRPC clients and servers, plus AWS JSON,
AWS Query, EC2 Query, and REST XML clients. See
[Protocol Status](/smithy-dotnet/protocols/status/) for supported surfaces and
maturity. Clients generated in another language need support for the same model
and wire protocol.

Follow the [Quick Start](/smithy-dotnet/getting-started/quick-start/) to build and
call your first service.
