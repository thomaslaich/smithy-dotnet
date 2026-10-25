---
title: Generated code
description: How NSmithy names generated C# when a Smithy name collides, and how to generate shapes that no service operation reaches.
---

For what each kind of shape becomes, see
[Code generation](/smithy-dotnet/concepts/code-generation/).

## Name collisions

A Smithy name that clashes with a name C# or the generator already uses gets a
suffix. Only the colliding name changes; wire names stay as modeled.

| Model | Generated C# |
| --- | --- |
| `structure DomainName { domainName: String }` | `DomainName.DomainNameValue` |
| A member, enum value, or shape named `Equals`, `ToString`, `GetHashCode`, or another inherited member | `EqualsValue` |
| A union variant named `value`, `tag`, `unknown`, `match`, or like its union | Variant class `ValueCase`, factory `FromValue` |

A shape that would generate a type named `Schemas` fails generation, because
that name is reserved for the schema namespace.

## Types without a service

Generation starts from the service and covers the shapes its operations reach.
To generate other shapes, such as the payloads of events a service publishes,
declare a shape closure in the model and name it in the `closure` setting:

```smithy title="model/events.smithy"
$version: "2"

metadata shapeClosures = [
    {
        id: "example.orders#events"
        includeBySelector: "structure[trait|tags|(values) = event]"
    }
]
```

```json title="smithy-build.json"
{
  "version": "1.0",
  "plugins": {
    "csharp-codegen": { "closure": "example.orders#events" }
  }
}
```

A project with a closure and no `SmithyService` generates only the types in the
closure and their schemas. With both, the closure is the generated set and the
service, which it must contain, still gets its client and server. A closure
covering the service's namespace generates the service together with every
shape declared there:

```smithy
metadata shapeClosures = [
    { id: "example.orders#all", includeNamespaces: ["example.orders"] }
]
```

Closures are part of the model, so a contracts package ships them with its
model files.
