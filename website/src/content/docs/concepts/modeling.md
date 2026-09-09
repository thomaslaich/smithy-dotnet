---
title: Modeling contracts
description: Define data, operations, and resources with Smithy shapes and traits.
---

A *shape* is a named definition: a data type, operation, resource, or service.
A *trait* annotates a shape or member with additional meaning, such as a constraint
or protocol binding.

## Define the contract

This model describes reading a book without choosing a wire protocol:

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

The resource declares identity (`id`), state (`title`), and its read operation.
It does not define a serialized object. The operation's input and output
structures define what crosses the service boundary.

`:= for Book` binds an inline structure to the resource. [Target elision](https://smithy.io/2.0/spec/idl.html#target-elision)
with `$id` and `$title`
reuses the corresponding resource member targets; Smithy validates that they
agree. `@required` belongs to each operation member, so different operations can
have different presence requirements. The generated structure names are
`GetBookInput` and `GetBookOutput`.

`@readonly` describes operation behavior. `@error("client")` classifies the
error; it assigns no HTTP status.

## Apply traits inline or separately

A trait can appear directly before the shape or member it annotates. For example,
this declaration assigns an HTTP status to the error:

```smithy
@error("client")
@httpError(404)
structure BookNotFound {
    message: String
}
```

Alternatively, keep the original error declaration and add:

```smithy
apply BookNotFound @httpError(404)
```

Both forms attach the same trait to the same shape in the assembled model.
`apply` lets you annotate an existing definition without editing its declaration.
It can appear in the same file or another model file included in the build.
Use inline traits when they help explain a declaration; use `apply` when keeping
annotations together—such as a service's HTTP bindings—makes the model easier
to maintain.

Member targets use `$`: `GetBookInput$id` identifies the `id` member of
`GetBookInput`. It differs from `$id` inside `for Book`, which reuses a member
target from the resource. See the [Smithy IDL specification](https://smithy.io/2.0/spec/idl.html)
for trait application and target elision.

## Add protocol bindings

A protocol defines request and response encoding, operation dispatch, and error
representation. A service selects protocols through traits. Some protocols also
require operation and member bindings.

For the Book contract, these annotations add a REST JSON binding. They can live
in a separate `.smithy` file in the same namespace:

```smithy
$version: "2"
namespace example.library

apply Library @alloy#simpleRestJson
apply GetBook @http(method: "GET", uri: "/books/{id}", code: 200)
apply GetBookInput$id @httpLabel
apply BookNotFound @httpError(404)
```

The operation becomes `GET /books/{id}`; the error maps to HTTP 404.
`@httpLabel` binds the input member to the route parameter. Keeping these traits
in a separate file separates their authoring from the contract declaration;
they still belong to the same assembled model.

Alternatively, RPC v2 CBOR needs only a service protocol trait for this operation:

```smithy
apply Library @smithy.protocols#rpcv2Cbor
```

Its protocol defines dispatch routes and error encoding. The operation's data
and error definitions do not change.

Protocols differ in supported shapes, streaming, and required annotations; gRPC,
for example, requires protobuf field indices. See [Protocol status](/smithy-dotnet/protocols/status/)
for implementation coverage.

A service can declare multiple protocols. NSmithy generates shared operation
interfaces and protocol-specific bindings. See [Hosting multiple protocols](/smithy-dotnet/servers/hosting/)
to expose them from one implementation.

Continue with [Code generation](/smithy-dotnet/concepts/code-generation/) to see
how the model becomes C#. For the full language, see the
[Smithy specification](https://smithy.io/2.0/spec/).
