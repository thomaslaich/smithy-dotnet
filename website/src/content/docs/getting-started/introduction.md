---
title: Introduction
description: Design, share, and evolve API contracts with Smithy, then build their .NET implementation with NSmithy.
---

[Smithy](https://smithy.io/2.0/) is an interface definition language (IDL) developed
by AWS to keep evolving APIs consistent across SDKs in many programming languages.
A Smithy model defines a service’s operations, data types, errors, and behavior.
Code generators use that shared contract to produce SDKs for each language.

NSmithy brings Smithy to .NET, generating C# clients, data types, and ASP.NET Core
server interfaces from Smithy models as part of `dotnet build`.

For example, this contract defines a greeting service with one operation:

```smithy
$version: "2"
namespace example

service GreetingService {
    version: "2026-01-01"
    operations: [Greet]
}

operation Greet {
    input := {
        @required
        name: String
    }
    output := {
        @required
        message: String
    }
}
```

A client supplies a `name` and receives a `message`. The contract defines that
exchange; the server implementation decides how to compose the greeting.
Protocol traits, introduced later, specify how the request and response travel
over the wire.

## An API contract teams can share and evolve

One team may implement a service while several others build applications that
call it. Those teams need to agree on the interface, know which version they
depend on, and understand how proposed changes affect their applications.

A Smithy model gives them a contract they can review and version independently
of server code. Teams can discuss operations, inputs, outputs, and errors before
implementing the service. Once they agree on the contract, client and server
teams can generate code from it and work in parallel.

Teams can publish models as
[versioned dependencies](/smithy-dotnet/guides/distributing-contracts/), so each
consumer can choose when to adopt a new contract version.
[Smithy Diff](https://smithy.io/2.0/guides/evolving-models.html#using-smithy-diff)
can detect backward-compatibility issues between versions during review or in CI.
[Validators](https://smithy.io/2.0/guides/model-linters.html) can also enforce
shared modeling conventions across services.

## Why Smithy?

Smithy combines a dedicated language for API design with a service model that
can support multiple wire protocols. These are useful advantages when choosing
how to define and maintain contracts across services.

OpenAPI supports [design-first development](https://learn.openapis.org/best-practices.html),
but authoring and reviewing a large API description in YAML or JSON can be
cumbersome. Smithy's [modeling language](https://smithy.io/2.0/spec/idl.html)
provides compact syntax for operations and reusable data types, called *shapes*.
Annotations called *traits* add constraints, documentation, and protocol details.
Teams can maintain the Smithy model and
[generate OpenAPI descriptions](https://smithy.io/2.0/guides/model-translations/converting-to-openapi.html)
for compatible HTTP APIs that need OpenAPI tooling.

[OpenAPI describes HTTP APIs](https://spec.openapis.org/oas/v3.2.0.html), including
their paths, methods, and media types. It supports different payload formats,
but the contract still incorporates HTTP-specific choices.

[Protobuf and gRPC](https://grpc.io/docs/what-is-grpc/introduction/) also support
contract-first development: Protobuf defines messages and service interfaces,
while gRPC provides the RPC framework, using Protobuf by default. This is a
natural fit for services communicating over gRPC. An organization that also
exposes REST APIs, however, needs a way to describe those interfaces too.

Smithy [separates the service model from its wire protocol](https://smithy.io/2.0/index.html#what-does-protocol-agnostic-mean).
Protocol traits specify how operations and data are transmitted, and a service
can declare multiple protocols. With suitable generators, the same model can
produce clients and servers for different protocols, reducing the need to
maintain separate contracts for each interface.
Each protocol may require additional annotations, such as HTTP bindings or
protobuf field indices for gRPC.

The practical choice depends on generator support. Before adopting Smithy,
check that the available generators cover the languages, protocols, and features
your clients and servers need. For NSmithy, see
[Protocol Status](/smithy-dotnet/protocols/status/).

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
