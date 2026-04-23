# Hybrid Codegen Architecture

Smithy.NET uses Smithy and .NET at different layers of the pipeline.

## Decision

Today, Smithy.NET does not implement its primary C# generator as a Smithy Java
plugin.

Instead:

- Smithy CLI is responsible for model assembly, validation, projections, imports,
  prelude resolution, and Maven dependency handling.
- Smithy.NET reads the resulting Smithy JSON AST/build artifacts and performs C#
  and `.proto` generation inside the .NET toolchain.

This is the current architecture and the working default for the project today.
It is not the same as a final decision that Java-side Smithy plugin integration
should never be used.

## Why

### Keep the .NET backend in the .NET build

The main user workflow is `dotnet build`. The current MSBuild task runs Smithy,
generates sources into `obj/`, manages incremental inputs, cleans stale files,
and adds generated files to compilation.

Keeping code generation in .NET means:

- the full generation path stays inside MSBuild
- generated outputs are handled with normal .NET build semantics
- consumers do not need a Gradle plugin or a Java-packaged Smithy plugin to use
  generated C# in their projects

### Keep iteration and tests local to the target language

The generator is primarily a C# backend. Its tests can construct small Smithy
models directly, run the generator in-process, and compile the resulting C#
against the local runtime packages.

That loop is materially simpler than:

- packaging a Java Smithy plugin
- invoking it through Smithy build or Gradle
- moving generated outputs back into `dotnet build`
- expressing most generator tests as cross-toolchain integration tests

### Reuse Smithy where it adds the most value

Smithy already solves the hard model-front-end problems:

- IDL parsing
- model assembly
- trait validation
- projections and transforms
- Maven dependency resolution
- compatibility with the broader Smithy ecosystem

Smithy.NET intentionally reuses that front end rather than reimplementing it.

## Tradeoffs

This architecture is not free.

Current costs:

- Smithy.NET owns a JSON AST reader and its own internal model representation
- there is an extra boundary between Smithy semantic model handling and C#
  emission
- some Smithy semantics may require additional mapping work in .NET that a Java
  plugin would get more directly from Smithy libraries

These costs are accepted because they keep the main backend, tests, and build
integration in the environment where the generated code is consumed.

## Why Not A Smithy Java Plugin By Default

A Java plugin would simplify one narrow concern: direct access to Smithy's
semantic model and plugin APIs.

It would also introduce new complexity that does not fit the current product
shape well:

- Java/Gradle packaging and release management for the generator itself
- tighter coupling to Smithy Java internals and plugin APIs
- a more complex handoff from Java-generated artifacts back into `dotnet build`
- slower backend iteration for a C#-focused project

For Smithy.NET today, that is not yet proven to be a net simplification.

## Scope Of The Decision

This decision does not rule out:

- small Java-side helpers if a future feature truly needs them
- better use of Smithy build projections or plugins as model pre-processing
- evaluating a Smithy Java plugin while still keeping MSBuild and Smithy CLI as
  the user-facing build entrypoint
- revisiting the boundary if the JSON AST seam becomes a dominant maintenance
  cost

It only rules out treating "rewrite the generator as a Smithy Java plugin" as a
committed roadmap direction before there is evidence that it simplifies the
system overall.

## What A Reasonable Evaluation Would Look Like

If the project explores Java-side Smithy plugin integration, the likely shape is
not "replace MSBuild with Gradle."

A more realistic option would be:

- keep `dotnet build` and `SmithyNet.MSBuild` as the main user experience
- keep invoking Smithy CLI from the .NET build
- let Smithy run a custom Java plugin for the part of the pipeline where direct
  access to Smithy's semantic model might help
- compare that result against the current JSON AST plus .NET generator approach

That evaluation should answer at least these questions:

- Does a Java plugin remove meaningful semantic-model complexity, or only move
  it?
- Does it improve protocol correctness or trait handling in a way that matters?
- What does it do to test speed and backend iteration for C# generation?
- How much new packaging, versioning, and distribution complexity does it add?
- Can the project keep the same MSBuild-first developer experience?

## Related Docs

- [MSBuild Reference](../msbuild.md)
- [Known Limitations](../known-limitations.md)
- [Roadmap](../planning/roadmap.md)
