---
title: Introduction
description: Design, share, and evolve API contracts with Smithy, then build their .NET implementation with NSmithy.
---

[Smithy](https://smithy.io/2.0/) is an interface definition language (IDL) developed
by AWS to maintain its SDKs across many programming languages.
A Smithy model defines a service’s operations, data types, errors, and behavior;
code generators use that shared contract to produce client SDKs for each language.

Smithy is useful well beyond AWS SDKs. For example, this contract defines a
greeting service with one operation:

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

This contract can be versioned and distributed before any server exists.
The IDL captures the semantics of the exchange, and code generators turn it into
clients and server interfaces for the protocols the service declares.
Serialization, error behavior, authentication, and validation are encoded in the
contract as traits.

NSmithy brings Smithy to .NET, generating C# clients, data types, and ASP.NET Core
server interfaces from Smithy models as part of `dotnet build`. It is listed in
[awesome-smithy](https://github.com/smithy-lang/awesome-smithy) as the community
C# client and server generator.

## Why Smithy?

Smithy can carry the API governance of a whole organization: one modeling
language for every service, whatever protocol each one speaks. Four properties
make that possible.

### Schema first

One team may implement a service while several others build applications that
call it. A Smithy model gives them a contract they can review and version
independently of server code. Teams can discuss operations, inputs, outputs,
and errors before implementing the service, then generate code from the agreed
contract and work in parallel.

Smithy's [modeling language](https://smithy.io/2.0/spec/idl.html) is built for
this review. It provides compact syntax for operations and reusable data types,
called *shapes*, and annotations called *traits* for constraints, documentation,
and protocol details. Authoring and reviewing the same API as a large OpenAPI
description in YAML or JSON is more cumbersome.

Teams can publish models as
[versioned dependencies](/smithy-dotnet/guides/distributing-contracts/), so each
consumer chooses when to adopt a new contract version.
[Smithy Diff](https://smithy.io/2.0/guides/evolving-models.html#using-smithy-diff)
detects backward-compatibility issues between versions during review or in CI.

### Protocol-agnostic

Smithy [separates the service model from its wire protocol](https://smithy.io/2.0/index.html#what-does-protocol-agnostic-mean).
Protocol traits specify how operations and data are transmitted, and a service
can declare multiple protocols. With suitable generators, the same model
produces clients and servers for each of them, so an organization does not
maintain one contract per interface style. Each protocol may require additional
annotations, such as HTTP bindings or protobuf field indices for gRPC.

[OpenAPI](https://spec.openapis.org/oas/v3.2.0.html) describes HTTP APIs,
including paths, methods, and media types, so the contract incorporates
HTTP-specific choices. [Protobuf and gRPC](https://grpc.io/docs/what-is-grpc/introduction/)
also support contract-first development, but an organization that exposes REST
APIs as well needs a second way to describe those. Smithy covers both from one
model, and can still
[generate OpenAPI descriptions](https://smithy.io/2.0/guides/model-translations/converting-to-openapi.html)
for HTTP APIs that need OpenAPI tooling.

### Extensible

Traits are the extension point. Organizations define
[custom traits](https://smithy.io/2.0/spec/model.html#defining-traits) for their
own conventions, and
[validators](https://smithy.io/2.0/guides/model-linters.html) enforce those
conventions across every service in review or CI. Code generators read the same
traits, so a convention captured in the model reaches generated clients and
servers without extra tooling.

A data classification trait, for example, marks members that carry personal
data:

```smithy
@trait(selector: "structure > member")
structure pii {}

structure Customer {
    @required
    id: String

    @pii
    email: String
}
```

A validator can then require that every operation returning `@pii` members
declares an authorization trait, and a generator can redact those members from
logs.

Protocols are traits too. An organization that sends commands over a message
broker such as RabbitMQ can define a
[protocol trait](https://smithy.io/2.0/spec/protocol-traits.html#protocoldefinition-trait)
for that transport and write a generator for it. The service model stays the
same; only the protocol trait and the generator are new.

### Mature

Smithy is the IDL AWS uses to define its own services and generate its SDKs.
It has been open source since 2019 and reached a stable 2.0 specification in
2022, with a CLI, build tooling, and IDE support maintained alongside it.

The practical choice still depends on generator support. Before adopting Smithy,
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
