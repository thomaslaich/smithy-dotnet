---
title: Introduction
description: Design, share, and evolve API contracts with Smithy, then build their .NET implementation with NSmithy.
---

[Smithy](https://smithy.io/2.0/) is a language for defining API contracts. NSmithy
uses those contracts to generate C# clients, data types, and ASP.NET Core server
interfaces, so teams can agree on an API before implementing it. The core contract
defines operations, data, and errors independently of a wire protocol. Protocol
traits then describe how callers and servers exchange those values.

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

OpenAPI also supports [design-first development](https://learn.openapis.org/best-practices.html),
but authoring and reviewing large contracts in YAML or JSON can be cumbersome.
Smithy's compact language makes operations, data, and errors easier to express
and review directly. Its protocol-independent service model also lets teams
agree on that contract before choosing HTTP routes or message encoding, then
use it across multiple wire protocols.

Smithy's tooling also supports the governance around that contract:
[validators](https://smithy.io/2.0/guides/model-linters.html) can enforce shared
modeling rules, and [Smithy Diff](https://smithy.io/2.0/guides/evolving-models.html#using-smithy-diff)
can detect backward-compatibility issues between model versions. Teams can publish
models as versioned dependencies in their chosen artifact repository. This gives
API review and compatibility checks a place in the build process.

## A contract that can span protocols

[OpenAPI describes HTTP APIs](https://spec.openapis.org/oas/v3.2.0.html): paths,
HTTP methods, parameters, responses, and their media types. It supports JSON, XML,
binary payloads, and other formats, and does not require a REST architecture.
Its scope is still HTTP, so the contract incorporates HTTP-specific decisions.
Protobuf and gRPC also support defining interfaces before implementation, with
their own encoding and RPC conventions.

Smithy separates the service model from those wire-level choices. For example,
the Weather service in [Modeling contracts](/smithy-dotnet/concepts/modeling/)
defines `GetCity` with a city identifier, a response, and a possible error. That
same operation can be exposed through REST/JSON, RPC v2 CBOR, or gRPC by adding
protocol traits and the bindings each protocol requires. The meaning of the operation and
its data stays shared; routing, serialization, and error encoding depend on the
selected protocol.

A service can declare multiple protocols, allowing external REST clients and
internal RPC clients to use the same modeled operations. Generators and runtimes
must support each chosen protocol, and some require additional annotations, such
as protobuf field indices for gRPC. Smithy tooling can also
[derive OpenAPI descriptions](https://smithy.io/2.0/guides/model-translations/converting-to-openapi.html)
for the HTTP API, so a shared Smithy contract can feed existing OpenAPI tooling.

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
