---
title: Modeling contracts
description: Describe what a service does with shapes and traits, then bind the contract to a wire protocol separately.
---

A Smithy contract describes what a service does: its operations, the data they
exchange, and the errors they can return. How requests travel over the wire is
a separate decision, added to the model as protocol bindings. NSmithy generates
the same C# types and handler interfaces whichever bindings you choose.

A *shape* is a named definition: a data type, an operation, a resource, or a
service. A *trait* annotates a shape or member with additional meaning, such as
a constraint or a protocol binding.

## Define the contract

This model, adapted from the
[Smithy quickstart](https://smithy.io/2.0/quickstart.html), lets callers look
up a city by identifier:

```smithy
$version: "2"
namespace example.weather

service Weather {
    version: "2006-03-01"
    resources: [City]
}

resource City {
    identifiers: { cityId: CityId }
    properties: { name: String, coordinates: CityCoordinates }
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
        $name

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

The service lists its resources, the resource ties a city's identifier and
properties to the operation that reads it, and the operation defines what
crosses the service boundary. `:=` declares the inline structures `GetCityInput`
and `GetCityOutput`; `for City` lets `$cityId`, `$name`, and `$coordinates`
reuse the resource's member definitions through
[target elision](https://smithy.io/2.0/spec/idl.html#target-elision).

Traits carry the rest of the meaning. `@required` and `@pattern` constrain
values, `@readonly` states that the operation has no side effects, and
`@error("client")` classifies the failure. None of them says anything about
HTTP: `GetCity` has no method or route, and `NoSuchResource` has no status code.

## Bind a protocol

A protocol defines encoding, dispatch, and error representation. A service
selects one with a trait, and some protocols need further bindings on operations
and members. `apply` attaches a trait to an existing shape, so the bindings can
live in a second `.smithy` file in the same namespace:

```smithy
$version: "2"
namespace example.weather

apply Weather @alloy#simpleRestJson
apply GetCity @http(method: "GET", uri: "/cities/{cityId}", code: 200)
apply GetCityInput$cityId @httpLabel
apply NoSuchResource @httpError(404)
```

`GetCity` is now `GET /cities/{cityId}` with `cityId` taken from the path, and
`NoSuchResource` maps to 404. The same traits can be written inline above each
shape instead; a separate file keeps the contract readable and the bindings
reviewable together. `GetCityInput$cityId` names a member of a shape and is
unrelated to the `$cityId` elision inside `for City`.

RPC v2 CBOR needs only the service trait, because it dispatches on operation
names rather than routes:

```smithy
apply Weather @smithy.protocols#rpcv2Cbor
```

A service may declare both, and NSmithy serves them from one implementation; see
[Hosting multiple protocols](/smithy-dotnet/servers/hosting/). The
[Protocols](/smithy-dotnet/protocols/overview/) overview compares the options.
Continue with [Code generation](/smithy-dotnet/concepts/code-generation/) to
see what the model becomes in C#.
