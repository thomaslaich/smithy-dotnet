---
title: Code generation
description: What NSmithy generates from a Smithy model, and how generation runs inside the .NET build.
---

NSmithy turns an assembled Smithy model into C# types, client and server APIs,
and runtime schemas. Generation is part of `dotnet build`; there is no separate
command, and with the `NSmithy.SmithyCli` package nothing to install beyond
NuGet packages.

## What the model becomes

For the Weather model in [Modeling contracts](/smithy-dotnet/concepts/modeling/),
the generator emits:

| Model | Generated C# |
| --- | --- |
| Structures, including inline operation inputs and outputs | Records such as `GetCityInput`, `GetCityOutput`, and `CityCoordinates`. |
| `@error` structures | Exceptions such as `NoSuchResource`. Throw them in handlers; catch them in clients. |
| Lists and maps | `IReadOnlyList<T>` and `IReadOnlyDictionary<string, T>` wherever they are used. No class is generated for the list or map shape itself. |
| The service, when `SmithyGenerateServer` is on | A handler interface `IWeatherHandler` composed of one interface per operation, plus `AddWeatherHandler` and `MapWeather` extensions for ASP.NET Core. |
| The service, when `SmithyGenerateClient` is on | A `WeatherClient` with a typed async method per operation. |
| Every shape | A runtime schema describing its structure and traits. |

Generated types are the same under every protocol the service declares. The
protocol adapter supplies routes, encoding, and error envelopes at runtime; a
handler receives `GetCityInput` and returns `GetCityOutput` regardless of how
the request arrived. Handler implementations are application code and are never
overwritten.

Operation `@examples` entries become `<example>` code samples in the XML
documentation on generated client methods and their interfaces. Each sample
includes its title and description, constructs the supplied input, calls the
async client method, and shows the expected output or exception when provided.
Error examples wrap the call in a typed `try`/`catch` and describe the expected
exception properties inside the catch block. Samples use a `client` variable
representing an existing client instance.

### Name collisions

A Smithy name that clashes with a name C# or the generator already uses gets a
suffix. Only the colliding name changes; wire names stay as modeled.

| Model | Generated C# |
| --- | --- |
| `structure DomainName { domainName: String }` | `DomainName.DomainNameValue` |
| A member, enum value, or shape named `Equals`, `ToString`, `GetHashCode`, or another inherited member | `EqualsValue` |
| A union variant named `value`, `tag`, `unknown`, `match`, or like its union | Variant class `ValueCase`, factory `FromValue` |

A shape that would generate a type named `Schemas` fails generation, because
that name is reserved for the schema namespace.

## Runtime schemas

Codecs and protocol adapters do not reflect over the generated records. They
read the runtime schemas, which carry shape structure and trait values with
typed accessors for reading and constructing values. The generator therefore
does not emit a serializer for every shape and protocol combination; each
protocol implementation combines the schemas with its own encoding rules.

Each shape's schema is a class in the child `Schemas` namespace:
`Example.Weather.Person` has `Example.Weather.Schemas.PersonSchema`. Import that
namespace when you hand a schema to a codec.

Because trait values are model data, a schema can retain traits the generator
knows nothing about. A custom `@owner("catalog-team")` trait reaches the
running application without a generator change. Retaining a trait does not
implement it: a new constraint, protocol, or client feature needs a component
that interprets it, and traits that change the C# representation, such as
`@required`, need generator support.

## The toolchain

Two NuGet packages provide the tools generation needs:

- `NSmithy.MSBuild` contains the MSBuild integration, NSmithy's C# and Protobuf
  code-generation plugins, the Smithy OpenAPI and documentation plugins, the
  trait libraries used by NSmithy, and their transitive Java dependencies,
  stored as JARs and POMs in a local Maven repository inside the package.
- `NSmithy.SmithyCli` contains the Smithy CLI with its Java runtime. It is
  versioned by the Smithy version and is optional: a Smithy CLI installed some
  other way works too. See [Smithy CLI](/smithy-dotnet/reference/smithy-cli/).

Consumers do not install these plugins separately, nor Java when they use
`NSmithy.SmithyCli`. During generation,
MSBuild points the CLI at the bundled repository and an isolated Maven cache, so
the bundled plugins resolve from local files rather than remote repositories.
Model dependencies declared by the project are resolved separately; see
[Maven dependencies](/smithy-dotnet/reference/maven-dependencies/). Optional
Sphinx HTML generation requires Python.

## The build

Generation runs in three steps:

1. MSBuild collects model inputs, including contracts project references, and
   prepares the Smithy build configuration.
2. The Smithy CLI assembles and validates the model, applies configured
   projections, and runs the enabled plugins.
3. MSBuild includes the generated `.g.cs` files under `obj/` in C# compilation.

MSBuild tracks inputs and outputs, so unchanged models skip generation. The
project's `.smithy` files and build configuration are registered with
`dotnet watch`, so `dotnet watch build` regenerates on every model edit.

## Configure generation

MSBuild properties say what a project builds: `SmithyService` names the service,
and `SmithyGenerateClient` and `SmithyGenerateServer` select the generated APIs.
Settings that tune the output, such as `baseNamespace` to prefix generated C#
namespaces, go in the project's `smithy-build.json`. On every build NSmithy
merges both into one effective `smithy-build.json` under `obj/`. The
[MSBuild reference](/smithy-dotnet/reference/msbuild/) lists every property and
setting, and
[Distributing contracts](/smithy-dotnet/guides/distributing-contracts/) covers
model dependencies.
