---
title: Code generation
description: What NSmithy generates from a Smithy model, and how generation runs inside the .NET build.
---

NSmithy turns an assembled Smithy model into C# types, client and server APIs,
and runtime schemas. Generation is part of `dotnet build`; there is no separate
command and nothing to install beyond the NuGet package.

## What the model becomes

For the Weather model in [Modeling contracts](/smithy-dotnet/concepts/modeling/),
the generator emits:

| Model | Generated C# |
| --- | --- |
| Structures, including inline operation inputs and outputs | Records such as `GetCityInput`, `GetCityOutput`, and `CityCoordinates`. |
| `@error` structures | Exceptions such as `NoSuchResource`. Throw them in handlers; catch them in clients. |
| The service, when `SmithyGenerateServer` is on | A handler interface `IWeatherHandler` composed of one interface per operation, plus `AddWeatherHandler` and `MapWeather` extensions for ASP.NET Core. |
| The service, when `SmithyGenerateClient` is on | A `WeatherClient` with a typed async method per operation. |
| Every shape | A runtime schema describing its structure and traits. |

Generated types are the same under every protocol the service declares. The
protocol adapter supplies routes, encoding, and error envelopes at runtime; a
handler receives `GetCityInput` and returns `GetCityOutput` regardless of how
the request arrived. Handler implementations are application code and are never
overwritten.

## Runtime schemas

Codecs and protocol adapters do not reflect over the generated records. They
read the runtime schemas, which carry shape structure and trait values with
typed accessors for reading and constructing values. The generator therefore
does not emit a serializer for every shape and protocol combination; each
protocol implementation combines the schemas with its own encoding rules.

Because trait values are model data, a schema can retain traits the generator
knows nothing about. A custom `@owner("catalog-team")` trait reaches the
running application without a generator change. Retaining a trait does not
implement it: a new constraint, protocol, or client feature needs a component
that interprets it, and traits that change the C# representation, such as
`@required`, need generator support.

## The bundled toolchain

The `NSmithy.MSBuild` NuGet package contains:

- The Smithy CLI and its bundled Java runtime.
- NSmithy's C# and Protobuf code-generation plugins.
- The Smithy OpenAPI and documentation plugins, plus the trait libraries used
  by NSmithy.
- Their transitive Java dependencies, stored as JARs and POMs in a local Maven
  repository inside the package.

Consumers do not install Java, the Smithy CLI, or these plugins separately.
The package version fixes their versions. During generation, MSBuild points the
CLI at the bundled repository and an isolated Maven cache, so the bundled tools
resolve from local files rather than remote repositories. Model dependencies
declared by the project are resolved separately, and optional Sphinx HTML
generation requires Python.

## The build

Generation runs in three steps:

1. MSBuild collects model inputs, including contracts project references, and
   prepares the Smithy build configuration.
2. The bundled CLI assembles and validates the model, applies configured
   projections, and runs the enabled plugins.
3. MSBuild includes the generated `.g.cs` files under `obj/` in C# compilation.

MSBuild tracks inputs and outputs, so unchanged models skip generation. The
project's `.smithy` files and build configuration are registered with
`dotnet watch`, so `dotnet watch build` regenerates on every model edit.

## Configure generation

Set `SmithyGenerateClient` and `SmithyGenerateServer` to select generated APIs
and `SmithyBaseNamespace` to prefix generated C# namespaces. The
[MSBuild reference](/smithy-dotnet/reference/msbuild/) lists every property, and
[Distributing contracts](/smithy-dotnet/guides/distributing-contracts/) covers
model dependencies.
