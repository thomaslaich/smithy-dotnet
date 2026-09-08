---
title: Code generation
description: How the Smithy model becomes C# types and runtime schemas.
---

NSmithy turns an assembled Smithy model into C# types, client and server APIs,
and runtime schemas. Generation is part of the .NET build.

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
resolve from local files rather than remote repositories.

This makes the bundled generation toolchain hermetic: its dependencies come
from the package rather than the machine's installed tools. The complete build
also has project-specific inputs, including model dependencies. Optional Sphinx
HTML generation requires Python separately.

## From model to compiler input

The build proceeds in three steps:

1. MSBuild collects model inputs, including contracts project references, and prepares
   the Smithy build configuration.
2. The bundled CLI assembles and validates the model, applies configured
   projections, and runs the enabled plugins.
3. MSBuild includes the generated `.g.cs` files under `obj/` in C# compilation.

The C# generator is a Java Smithy build plugin invoked through this process.
It runs during `dotnet build`; consumers do not need a separate generation command.
MSBuild tracks inputs and outputs to skip generation when they are unchanged.

For continuous builds, run:

```shell
dotnet watch build
```

NSmithy registers the project's `.smithy` files and Smithy build configuration
with `dotnet watch`. Editing them triggers a new build, including code generation.

Edit the model or generator configuration to change generated code. Handler
implementations supplied by the project templates are application code and are
not overwritten by generation.

## C# types and runtime schemas

The generator emits two complementary representations:

- **C# types** represent modeled values, operation inputs and outputs, and errors.
  Generated client and handler interfaces use these types.
- **Runtime schemas** describe shape structure and traits, with typed accessors
  for reading and constructing values. Codecs use these schemas without runtime
  reflection.

Smithy's trait values are model data. NSmithy can retain additional traits in
schemas without changing the generator for each trait. Retaining a trait does
not implement its behavior: a new constraint, protocol, or client feature needs
a consumer that interprets it. Traits that affect C# representation may also
require generator changes.

Protocol implementations combine schemas with serialization and transport
rules. The generator therefore does not need to emit a separate serializer for
every shape and protocol combination.

## Configure generation

Set `SmithyGenerateClient` and `SmithyGenerateServer` to select generated APIs.
Use `SmithyBaseNamespace` to prefix generated C# namespaces. See the
[MSBuild reference](/smithy-dotnet/reference/msbuild/)
for configuration and [Distributing contracts](/smithy-dotnet/guides/distributing-contracts/)
for model dependencies.
