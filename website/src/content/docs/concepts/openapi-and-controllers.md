---
title: OpenAPI and controllers
description: Why NSmithy favors contract-first APIs, and how a Smithy model compares with OpenAPI and code-first ASP.NET Core in ergonomics, expressiveness, and protocol support.
---

Most .NET HTTP APIs are written code first: controllers or minimal APIs define
the endpoints, [Microsoft.AspNetCore.OpenApi](https://learn.microsoft.com/aspnet/core/fundamentals/openapi/overview)
describes them as an OpenAPI document, and tools such as
[Kiota](https://learn.microsoft.com/openapi/kiota/overview) or
[NSwag](https://github.com/RicoSuter/NSwag) generate clients from it. The
contract-first alternative writes an OpenAPI document by hand and generates
code from that.

Our position is that contract first is the better way to build APIs in an
organization of many teams, and that Smithy with NSmithy is the better way to
do contract first: easier to write and review, able to say more about how an
API behaves, and independent of the wire protocol.

## Contract first

When one team builds a service and several others call it, the contract is the
thing they share. Contract first lets them review it before the server exists,
generate clients and servers from the agreed version, and work in parallel.
Each consumer adopts a new [contract version](/smithy-dotnet/guides/distributing-contracts/)
when it chooses, and [Smithy Diff](https://smithy.io/2.0/guides/evolving-models.html#using-smithy-diff)
flags breaking changes in review or CI.

In a code-first API the implementation is the contract. The OpenAPI document is
derived from the code, so a renamed property, a changed route, or a serializer
setting changes the contract whether or not anyone meant it to. The document
describes what the generator infers from attributes and types, while routing,
binding, and serialization are configured separately, and clients generated from
the document inherit any gap between the two.

## Ergonomics

The same operation, a read with a path parameter and a modeled 404, in Smithy:

```smithy
@readonly
@http(method: "GET", uri: "/orders/{orderId}")
operation GetOrder {
    input := {
        @required
        @httpLabel
        orderId: String
    }
    output := {
        @required
        status: String
    }
    errors: [NoSuchOrder]
}

@error("client")
@httpError(404)
structure NoSuchOrder {
    message: String
}
```

and in OpenAPI:

```yaml
paths:
  /orders/{orderId}:
    get:
      operationId: GetOrder
      parameters:
        - name: orderId
          in: path
          required: true
          schema:
            type: string
      responses:
        "200":
          description: OK
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/GetOrderOutput"
        "404":
          description: NoSuchOrder
          content:
            application/json:
              schema:
                $ref: "#/components/schemas/NoSuchOrder"
components:
  schemas:
    GetOrderOutput:
      type: object
      required: [status]
      properties:
        status:
          type: string
    NoSuchOrder:
      type: object
      properties:
        message:
          type: string
```

The Smithy version reads as an operation with an input, an output, and an
error. The OpenAPI version reads as an HTTP exchange, and the operation has to
be reassembled from paths, parameters, responses, and schema references.

NSmithy adds to that on the .NET side:

- **Generation is part of `dotnet build`.** There is no separate generator
  command and no generated code to check in.
- **The server implements the contract.** Generated
  [handler interfaces](/smithy-dotnet/servers/) take modeled inputs and return
  modeled outputs, so a missing operation or a changed signature is a compile
  error. The runtime does the routing, serialization, and
  [validation](/smithy-dotnet/servers/validation/) the model specifies.
- **Client and server come from one generator** and one runtime, checked
  against Smithy's protocol conformance suites; see
  [Protocol status](/smithy-dotnet/protocols/status/).
- **Contracts are packages.** A model ships as a NuGet package or a Maven JAR,
  and consumers reference it like any other dependency.

## Expressiveness

A Smithy model describes how an API behaves, not only the shape of its data.
NSmithy generates code from these traits:

| Trait | What NSmithy generates |
| --- | --- |
| [`@paginated`](/smithy-dotnet/guides/client-configuration/pagination/) | `IAsyncEnumerable` paginators over pages and items |
| [`@waitable`](/smithy-dotnet/guides/client-configuration/waiters/) | Waiters that poll an operation until a resource reaches a state |
| [`@retryable`](/smithy-dotnet/guides/client-configuration/retry/) | Retry and throttling classification for modeled errors |
| `@idempotencyToken` | A token the client fills in, so retries are safe |
| Constraint traits | [Server-side validation](/smithy-dotnet/servers/validation/) before the handler runs |
| [`@examples`](/smithy-dotnet/servers/fake-handlers/) | [Fake clients](/smithy-dotnet/guides/client-configuration/fake-clients/) and fake handlers for tests |
| [`@smokeTests`](/smithy-dotnet/guides/client-configuration/smoke-tests/) | Smoke tests against a running service |
| [`@endpointRuleSet`](/smithy-dotnet/guides/client-configuration/endpoint-rules/) | Endpoint resolution from client parameters |

Waiters, fake clients, smoke tests, and endpoint resolvers are generated for
C# only so far; see [F#](/smithy-dotnet/guides/fsharp/).

OpenAPI's schema keywords cover the constraints. The rest has no standard
OpenAPI equivalent and is usually expressed through vendor extensions, which
each generator reads differently or not at all. Organizations can add
[their own traits](/smithy-dotnet/reference/extending/) the same way Smithy
defines these.

## Protocol agnostic

OpenAPI describes HTTP, so an OpenAPI contract is an HTTP contract. A Smithy
model keeps the service separate from its wire protocol: the same operations
can be served as REST JSON, RPC v2 CBOR or JSON, and gRPC, from
[one handler set](/smithy-dotnet/servers/hosting/). An organization with REST
and gRPC services keeps one contract format for both, and Smithy still
[generates OpenAPI](/smithy-dotnet/guides/endpoint-documentation/) for HTTP
tooling that needs it.

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
