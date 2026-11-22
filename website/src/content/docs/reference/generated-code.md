---
title: Generated code
description: What each kind of Smithy shape becomes in C#, how colliding names are renamed, how to generate shapes that no service operation reaches, and how the output is formatted.
---

## Shapes

For the Weather model in [Modeling contracts](/smithy-dotnet/concepts/modeling/),
the generator emits:

| Model | Generated C# |
| --- | --- |
| Structures, including inline operation inputs and outputs | Records such as `GetCityInput`, `GetCityOutput`, and `CityCoordinates` |
| `@error` structures | Exceptions such as `NoSuchResource`, thrown by handlers and caught by clients |
| Enums and int enums | A record struct and a C# enum, with members in Pascal case: `IN_PROGRESS` becomes `InProgress`, and the wire value stays as modeled |
| Lists and maps | `IReadOnlyList<T>` and `IReadOnlyDictionary<string, T>` where they are used, with no class for the list or map shape itself |
| The service, when `SmithyGenerateServer` is on | A handler interface `IWeatherHandler` composed of one interface per operation, plus `AddWeatherHandler` and `MapWeather` extensions for ASP.NET Core |
| The service, when `SmithyGenerateClient` is on | A `WeatherClient` with a typed async method per operation |
| Every shape | A schema class |

`@documentation` becomes the XML documentation of the generated types and
members. HTML in it, as the AWS models carry, is converted: `<p>` to `<para>`,
lists to `<list>`, `<code>` to `<c>` and links to `<see href>`. Tags NSmithy
does not know, such as the `<account-id>` placeholders in ARN examples, stay
literal text.

Operation `@examples` become `<example>` code samples in the XML documentation
of the generated client methods. Each sample constructs the input, calls the
method on a `client` variable, and shows the expected output, or for an error
example a typed `try`/`catch` with the expected exception properties.

## Member optionality

A structure member is nullable unless it is `@required` or has a `@default`.
A project that generates a client and neither a server nor any operation of
its own follows Smithy's
[client rules](https://smithy.io/2.0/spec/aggregate-types.html#structure-member-optionality)
instead: every member of an operation input and every `@clientOptional` member
is nullable, so a model that later drops `@required` from such a member still
compiles against the client's code. An input member with a default stays
nullable in C#, and the client sends the default when it is null.

| `SmithyGenerateClient` | `SmithyGenerateServer` | Rules |
| --- | --- | --- |
| on | off | Client rules, unless the project owns an operation |
| off | on | Server rules |
| on | on | Server rules |
| off | off | Server rules |

Shapes from a referenced project or package keep the types their owner
generated, so a client that shares types with a server gets the server rules.
[Distributing contracts](/smithy-dotnet/guides/distributing-contracts/#nuget-distribution)
describes how a client gets the client rules for a published contract.

## Schemas

Each shape's schema is a class in the child `Schemas` namespace:
`Example.Weather.Person` has `Example.Weather.Schemas.PersonSchema`. Import that
namespace when you hand a schema to a codec.

A schema carries the shape's members and its trait values, with typed accessors
for each trait. It leaves out traits used only at build time, such as
`@documentation`, `@examples`, `@tags`, `@deprecated`, and test traits.
[How NSmithy works](/smithy-dotnet/concepts/how-it-works/#schemas) explains
what reads them.

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

## Formatting

After generating, NSmithy formats the files with
[CSharpier](https://csharpier.com/) for C# and
[Fantomas](https://fsprojects.github.io/fantomas/) for F# when the formatter is
installed, either on `PATH` or as a `dotnet` tool. Without one, the generated
files build the same but are left unformatted, with long lines. A local tool
pins the formatter's version for everyone building the repository:

```sh
dotnet new tool-manifest   # once per repository
dotnet tool install csharpier   # or fantomas
```
