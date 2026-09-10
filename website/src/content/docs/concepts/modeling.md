---
title: Modeling contracts
description: Define a protocol-independent contract with Smithy shapes and traits, then add wire protocol bindings.
---

A Smithy contract describes what a service does independently of how requests
and responses travel over the wire. You can define its data, operations, and
errors first, then add protocol bindings for REST/JSON, RPC v2 CBOR, or another
supported protocol.

A *shape* is a named definition: a data type, operation, resource, or service.
A *trait* annotates a shape or member with additional meaning, such as a constraint
or protocol binding.

## Define the contract

The [Smithy quickstart](https://smithy.io/2.0/quickstart.html) models a Weather
service with cities and forecasts. This page uses a smaller version of that
example, focused on `GetCity`. The full tutorial adds forecasts, city listing,
pagination, and an operation to retrieve the current time.

Start with the information a caller needs: a city identifier goes in; a name and
coordinates come back; an unknown city produces a modeled error. No wire protocol
is needed to describe that interaction.

```smithy
$version: "2"
namespace example.weather

service Weather {
    version: "2006-03-01"
    resources: [City]
}

resource City {
    identifiers: { cityId: CityId }
    properties: { coordinates: CityCoordinates }
    read: GetCity
}

@pattern("^[A-Za-z0-9 ]+$")
string CityId

structure CityCoordinates {
    @required
    latitude: Float

    @required
    longitude: Float
}

@readonly
operation GetCity {
    input := for City {
        @required
        $cityId
    }
    output := for City {
        @required
        @notProperty
        name: String

        @required
        $coordinates
    }
    errors: [NoSuchResource]
}

@error("client")
structure NoSuchResource {
    @required
    resourceType: String
}
```

The contract above is **protocol agnostic**. `GetCity` has no HTTP method or URL,
and `CityCoordinates` has no chosen JSON or binary representation. Even the
resource's `read` relationship describes behavior without prescribing an HTTP
`GET`. Protocol bindings add those decisions to the model later, while keeping
the operation's inputs, outputs, and errors shared.

## Read the model

The declarations describe different parts of the contract:

| Shape | Role in this API |
| --- | --- |
| `Weather` | Selects the resources and operations that belong to the service. |
| `City` | Connects a city's identity and state to its read operation. |
| `GetCity` | Defines the request, response, and possible error. |
| `CityId` | Gives the identifier a reusable name and a string constraint. |
| `CityCoordinates` | Groups latitude and longitude into a structured value. |
| `NoSuchResource` | Describes an error callers can handle explicitly. |

A resource does not define a serialized object or a database table. The
operation's input and output structures define what crosses the service boundary.
Here, the request contains `cityId`, while the response contains `name` and
`coordinates`.

`:=` declares an inline structure, named `GetCityInput` or `GetCityOutput` for
this operation. `for City` associates it with the resource.
[Target elision](https://smithy.io/2.0/spec/idl.html#target-elision) lets `$cityId`
and `$coordinates` reuse the types declared on `City`. The `name` member has
`@notProperty` because it is additional response data, not a declared resource
property.

Traits make these definitions more precise. `@required` sets a member's presence
requirement; `@pattern` constrains the identifier's text. `@readonly` says the
operation has no side effects. `@error("client")` classifies the error without
assigning an HTTP status. These annotations give generators and validators
information beyond the types alone.

## Apply traits inline or separately

A trait can appear directly before the shape or member it annotates. For example,
this declaration assigns an HTTP status to the error:

```smithy
@error("client")
@httpError(404)
structure NoSuchResource {
    @required
    resourceType: String
}
```

Alternatively, keep the original error declaration and add:

```smithy
apply NoSuchResource @httpError(404)
```

Both forms attach the same trait to the same shape in the assembled model.
`apply` lets you annotate an existing definition without editing its declaration.
It can appear in the same file or another model file included in the build.
Use inline traits when they help explain a declaration; use `apply` when keeping
annotations together—such as a service's HTTP bindings—makes the model easier
to maintain.

Member targets use `$`: `GetCityInput$cityId` identifies the `cityId` member of
`GetCityInput`. It differs from `$cityId` inside `for City`, which reuses a member
target from the resource. See the [Smithy IDL specification](https://smithy.io/2.0/spec/idl.html)
for trait application and target elision.

## Add protocol bindings

A protocol defines request and response encoding, operation dispatch, and error
representation. A service selects protocols through traits. Some protocols also
require operation and member bindings.

For the Weather contract, these annotations add a REST JSON binding. They can live
in a separate `.smithy` file in the same namespace:

```smithy
$version: "2"
namespace example.weather

apply Weather @alloy#simpleRestJson
apply GetCity @http(method: "GET", uri: "/cities/{cityId}", code: 200)
apply GetCityInput$cityId @httpLabel
apply NoSuchResource @httpError(404)
```

The operation becomes `GET /cities/{cityId}`; the error maps to HTTP 404.
`@httpLabel` binds the input member to the route parameter. Keeping these traits
in a separate file separates their authoring from the contract declaration;
they still belong to the same assembled model.

Alternatively, RPC v2 CBOR needs only a service protocol trait for this operation:

```smithy
apply Weather @smithy.protocols#rpcv2Cbor
```

The same data and error definitions can serve multiple protocols. See
[Hosting multiple protocols](/smithy-dotnet/servers/hosting/) to expose them from
one implementation, or continue with [Code generation](/smithy-dotnet/concepts/code-generation/)
to see how the model becomes C#.
