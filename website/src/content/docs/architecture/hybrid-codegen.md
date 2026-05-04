---
title: Hybrid Codegen Architecture
description: How NSmithy splits responsibility between the Smithy CLI and the .NET toolchain.
---

NSmithy uses Smithy and .NET at different layers of the pipeline.

## Overview

NSmithy implements C# code generation as a Smithy Java plugin
(`smithy-csharp-codegen`). The plugin is loaded by the Smithy CLI and runs
inside `smithy build`, where it has direct access to Smithy's semantic model via
the standard `CodegenDirector` APIs.

The two sides of the architecture are:

- **Java (codegen)**: model assembly, validation, trait resolution, and C#/`.proto`
  emission all happen inside the Java plugin during `smithy build`.
- **.NET (runtime + build integration)**: `NSmithy.MSBuild` invokes `smithy build`,
  picks up the generated files, and adds them to the `dotnet build` compilation.
  The `.NET` runtime packages (`NSmithy.Core`, `NSmithy.Protocols.*`, etc.) provide
  the protocol dispatch, schema metadata, and transport abstractions that the
  generated code depends on.

## Codegen Pipeline

```
smithy-build.json
      │
      ▼
smithy build (Smithy CLI)
      │   loads smithy-csharp-codegen Java plugin
      │   assembles + validates model
      │   runs CodegenDirector
      │     ├── StructureGenerator    → <Shape>.g.cs
      │     ├── UnionGenerator        → <Shape>.g.cs
      │     ├── ErrorGenerator        → <Shape>.g.cs
      │     ├── List/MapGenerator     → <Shape>.g.cs
      │     ├── Enum/IntEnumGenerator → <Shape>.g.cs
      │     ├── ClientGenerator       → <Service>Client.g.cs
      │     ├── ServerGenerator       → <Service>Server.g.cs
      │     └── ProtoGenerator        → <Service>.proto  (gRPC only)
      │
      ▼
  obj/Smithy/<projection>/csharp-codegen/**/*.g.cs
  obj/Smithy/<projection>/csharp-codegen/**/*.proto
```

MSBuild then picks up the generated files via two targets in `NSmithy.MSBuild`:

- `_AddSmithyGeneratedCompileItems` – adds `.g.cs` files to `<Compile>`.
- `_AddSmithyGeneratedProtoItems` – registers `.proto` files with Grpc.Tools.

## Java Plugin

`CSharpClientCodegenPlugin` implements Smithy's `SmithyBuildPlugin` interface
and is discovered on the classpath via
`META-INF/services/software.amazon.smithy.build.SmithyBuildPlugin`.

It delegates to `DirectedCSharpClientCodegen`, which implements
`DirectedCodegen`. Smithy's `CodegenDirector` calls one method per shape kind
(structure, union, list, map, enum, service, etc.) and the plugin emits the
corresponding `.g.cs` file through `CSharpWriter`.

Because the plugin runs inside `smithy build`, it has direct access to Smithy's
fully assembled and validated semantic model — no separate JSON AST parsing step
is needed in .NET.

## MSBuild Integration

The main user workflow is `dotnet build`. `NSmithy.MSBuild` provides a
`GenerateSmithyCode` target that runs `smithy build` before `CoreCompile`.
Incremental builds are tracked via a stamp file; the target only re-runs when
model inputs change.

Consumers do not need Gradle or a separate codegen invocation — they reference
`NSmithy.MSBuild` as a NuGet package and add `smithy-csharp-codegen` as a Maven
dependency in their `smithy-build.json`.

## .NET Runtime Packages

Generated code depends on .NET packages that are published to NuGet:

- `NSmithy.Core` — `Schema`, `ShapeId`, `Trait`, codec interfaces
- `NSmithy.Http` — `IHttpTransport`, `SmithyHttpRequest`, `SmithyHttpResponse`
- `NSmithy.Client` — `ISmithyClient`, `SmithyClientOptions`
- `NSmithy.Server` / `NSmithy.Server.AspNetCore` — server framework
- `NSmithy.Codecs.Json/Xml/Cbor` — codec implementations
- `NSmithy.Protocols.RestJson/RestXml/RpcV2Cbor` — protocol binding

These packages are independent of the Java plugin. A consumer project references
them in its `.csproj`; the generated `.g.cs` files import the matching types.

## Tradeoffs

**What the Java plugin approach gives us:**

- direct access to Smithy's semantic model — no reimplementing IDL parsing,
  shape assembly, or trait resolution in .NET
- protocol correctness from Smithy's own model (trait semantics are authoritative)
- interoperability with the Smithy plugin ecosystem (transforms, projections,
  external trait libraries like `alloy`)

**What it costs:**

- the generator and its tests live in a Java/Gradle build, which is a separate
  toolchain from the .NET runtime
- backend iteration (edit generator → test generated output) involves building
  the JAR and invoking `smithy build`, which is slower than an in-process loop
- packaging and release management spans NuGet (runtime) and Maven Central
  (codegen JAR)

These costs are accepted because the Java plugin gives access to Smithy's
semantic model at the layer where model semantics are most precisely defined.

## Related Docs

- [MSBuild Reference](../msbuild/)
- [Known Limitations](../reference/known-limitations/)
- [Roadmap](../contributing/roadmap/)
