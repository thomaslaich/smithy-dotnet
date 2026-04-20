# Plan: Build Smithy.NET

Smithy.NET will be a .NET code generation and runtime toolkit for Smithy models. The first release should focus on a narrow, working vertical slice: validated Smithy model input, generated C# types, one JSON codec path, and one generated client path. Server runtime and F# ergonomics should follow after the core generator is proven.

The architecture remains hybrid:

- An MSBuild package invokes the Smithy CLI, validates and assembles models, consumes JSON AST/build artifacts, and writes generated C# into `obj/`.
- A Roslyn incremental source generator adds compile-time helpers where Roslyn is a better fit, mainly serializer metadata, diagnostics, and integration glue.

Target modern supported .NET versions. As of 2026, .NET 10 is the current LTS; keep `net8.0` only if the project intentionally supports existing LTS users until its November 2026 end of support.

---

## MVP Principles

1. Build one complete user workflow before broad protocol coverage:
   - Add `.smithy` files to a project.
   - Build the project.
   - Consume generated C# models and a generated client.
   - Serialize and deserialize at least one protocol payload.

2. Use Smithy CLI and Smithy build projections first:
   - Do not port the Smithy IDL parser early.
   - Let Smithy handle model assembly, prelude resolution, imports, Maven dependencies, projections, and validation.

3. Keep generated code inspectable but not source-controlled by default:
   - Default output: `$(IntermediateOutputPath)/Smithy/`.
   - Optional generated source output for debugging, CI snapshots, or committed generated code.

4. Treat protocol compliance as product scope, not polish:
   - Add Smithy protocol compliance tests as soon as the first protocol is implemented.
   - Do not declare protocol support until request/response/error edge cases are covered.

5. Keep protocol support useful but opinionated:
   - Generated clients should support both `aws.protocols#restJson1` and `alloy#simpleRestJson`.
   - Generated servers should target `alloy#simpleRestJson` first.
   - Do not prioritize `restJson1` server generation until there is concrete demand.

---

## Phases

### Phase 0: Repository and Engineering Baseline (Week 1)

1. Create solution and project layout:
   - `src/SmithyNet.Core`
   - `src/SmithyNet.CodeGeneration`
   - `src/SmithyNet.MSBuild`
   - `src/SmithyNet.Generators`
   - `src/SmithyNet.Json`
   - `src/SmithyNet.Http`
   - `src/SmithyNet.Client`
   - `tests/*`

2. Add baseline engineering:
   - Central package management.
   - Nullable reference types and analyzers enabled.
   - Deterministic builds and SourceLink.
   - Unit test project and snapshot/golden-file test helper.
   - CI for build, test, formatting, and package validation.

3. Decide public naming:
   - Product name: Smithy.NET.
   - Repository name: `smithy-dotnet`.
   - NuGet IDs and C# namespaces: `SmithyNet.*`.

### Phase 1: Smithy Model Input and IR (Weeks 2-4)

1. Create `SmithyNet.Core`:
   - `ShapeId`
   - shape/type metadata used by generated code
   - trait metadata needed at runtime
   - protocol/codec abstractions
   - `Document` value type for Smithy `document`

2. Create `SmithyNet.CodeGeneration`:
   - Read Smithy JSON AST/build output.
   - Convert it into a small internal representation (IR) for generation.
   - Preserve enough metadata for nullability, defaults, traits, service closure, errors, and protocols.

3. Use Smithy CLI through Smithy build:
   - Support `smithy-build.json`.
   - Support projections.
   - Support `sources`, `imports`, and Maven dependencies through Smithy CLI behavior.
   - Emit clear diagnostics when the Smithy CLI, Java if required by the selected distribution, or model validation fails.

### Phase 2: C# Shape Generation (Weeks 5-8)

1. Generate C# for model shapes:
   - structures
   - lists
   - maps
   - string enums
   - int enums
   - unions
   - error shapes

2. Define nullability and default semantics explicitly:
   - Respect Smithy 2.0 required/default behavior.
   - Support `@required`, `@default`, `@clientOptional`, input/output structures, and sparse collections.
   - Generate nullable reference annotations consistently.

3. Prefer immutable generated types:
   - C# records for structures where practical.
   - Constructors or builders for ergonomic creation.
   - Stable collection interfaces with immutable copies at boundaries.

4. Generate unions as closed tagged types:
   - Include an unknown variant for forward compatibility.
   - Provide pattern matching helpers.
   - Keep F# consumption in mind, but do not generate F# wrappers yet.

5. Add golden-file tests for generated output.

### Phase 3: MSBuild Integration (Weeks 9-11)

1. Create `SmithyNet.MSBuild` NuGet package:
   - Add `build`/`buildTransitive` targets.
   - Expose `<SmithyModel Include="..." />`.
   - Expose optional properties:
     - `SmithyBuildFile`
     - `SmithyProjection`
     - `SmithyGeneratedOutputPath`
     - `SmithyEmitGeneratedFiles`
     - `SmithyCliPath`

2. Generate into `$(IntermediateOutputPath)/Smithy/` and add files to `Compile`.

3. Implement incremental builds:
   - Use MSBuild `Inputs`/`Outputs`.
   - Track `.smithy`, `.json`, `smithy-build.json`, imported model files, and generator version.
   - Write a dependency manifest for transitive model inputs discovered by Smithy.

4. Rely on an external Smithy CLI:
   - Preferred developer experience: install `smithy-cli` in a managed project environment such as Pixi with conda-forge.
   - Resolve `smithy` from `PATH` by default.
   - Keep `SmithyCliPath` as an explicit override for locked-down or non-standard build environments.
   - Document Java requirements when they apply to the selected CLI distribution.

### Phase 4: JSON Codec and Serialization Generation (Weeks 12-15)

1. Create `SmithyNet.Json`:
   - Use `System.Text.Json`.
   - Implement Smithy-specific converters for timestamps, blobs, documents, enums, unions, sparse collections, and protocol-specific names.
   - Support traits such as `@jsonName` and `@timestampFormat`.

2. Create `SmithyNet.Generators` as an incremental generator:
   - Discover generated Smithy types from marker attributes.
   - Generate `JsonSerializerContext` partial types or registration glue for AOT-friendly serialization.
   - Emit diagnostics for unsupported shapes/traits.

3. Do not model CBOR as a source-generated serializer context:
   - `System.Formats.Cbor` requires explicit reader/writer logic.
   - Add CBOR later with protocol compliance tests.

### Phase 5: First Client Protocols (Weeks 16-20)

1. Create `SmithyNet.Http`:
   - `HttpClient` based transport.
   - Request/response abstraction.
   - Cancellation and timeout flow.

2. Create `SmithyNet.Client`:
   - Operation invoker.
   - Middleware pipeline.
   - Error deserialization.
   - Retry hooks, but keep the first retry implementation minimal.

3. Implement `restJson1` as the first client protocol:
   - Cover the HTTP/JSON request, response, and error paths needed for a generated client.
   - Keep AWS-compatible client generation available for services that use `aws.protocols#restJson1`.
   - Keep Smithy RPC v2 CBOR as a later protocol once the HTTP/JSON path is proven.

4. Implement `alloy#simpleRestJson` as the second client protocol:
   - Reuse the HTTP binding machinery from `restJson1` where the wire behavior is shared.
   - Keep protocol-specific behavior isolated so client and server generation can share binding logic.

5. Generate service clients:
   - service interface
   - concrete client
   - operation request/response types
   - error dispatch
   - endpoint/base URI configuration

6. Add protocol compliance tests for generated clients:
   - request serialization
   - response deserialization
   - error deserialization and dispatch
   - cover both `restJson1` and `simpleRestJson` as support is added

### Phase 6: Packaging and First Preview (Weeks 21-22)

1. Ship preview packages:
   - `SmithyNet.Core`
   - `SmithyNet.CodeGeneration`
   - `SmithyNet.MSBuild`
   - `SmithyNet.Generators`
   - `SmithyNet.Json`
   - `SmithyNet.Http`
   - `SmithyNet.Client`

2. Add examples:
   - Generated model-only library.
   - Generated client against a small test service.
   - NativeAOT sample if serializer generation is expected to support it.

3. Document:
   - Quick start.
   - MSBuild properties.
   - Supported Smithy traits.
   - Supported protocols.
   - Known limitations.

### Phase 7: Server Runtime and End-to-End Example (Post-MVP)

1. Create `SmithyNet.Server`:
   - operation dispatcher
   - protocol-aware request routing for `alloy#simpleRestJson`
   - validation hooks
   - error serialization

2. Create `SmithyNet.Server.AspNetCore`:
   - endpoint routing integration
   - middleware integration
   - generated handler interfaces

3. Generate server surfaces:
   - service handler interface
   - operation-scoped handler interfaces as the primary runtime contract
   - service-scoped aggregate handler interface for simple single-class implementations
   - DI helpers that register one service handler implementation for all operation handler interfaces
   - operation handler methods
   - request/response binding glue
   - error dispatch and serialization
   - target `alloy#simpleRestJson` before any AWS-flavored server protocol

4. Add an end-to-end example:
   - Smithy model library
   - generated ASP.NET Core server
   - generated client
   - one `alloy#simpleRestJson` HTTP/JSON round trip

### Phase 8: Additional Codecs and Protocols (Post-MVP)

1. Add `SmithyNet.Cbor`:
   - Explicit `CborReader`/`CborWriter` codecs.
   - Required for Smithy RPC v2 CBOR.

2. Add `SmithyNet.Xml`:
   - `@xmlName`
   - `@xmlAttribute`
   - `@xmlFlattened`
   - namespace handling

3. Add protocol packages:
   - `SmithyNet.Protocols.RpcV2Cbor`
   - `SmithyNet.Protocols.AwsJson`
   - `SmithyNet.Protocols.RestJson`
   - `SmithyNet.Protocols.RestXml`

4. Add auth packages:
   - `SmithyNet.Auth`
   - `SmithyNet.Auth.SigV4`

### Phase 9: F# Support (Post-MVP)

1. Start with consumption ergonomics:
   - `option<'T>` helpers
   - `Async` wrappers
   - F#-friendly client modules

2. Add generated F# wrappers later:
   - discriminated unions for Smithy unions
   - record wrappers for selected structures
   - computation expressions only if real examples justify them

3. Keep F# generation in the MSBuild pipeline:
   - F# does not use C# source generators for `.fs` source.
   - Generated `.fs` ordering must be explicit.

### Phase 10: Native Model Loading (Future)

Native Smithy parsing should remain a future project unless the Java/Smithy CLI dependency becomes a major adoption blocker. If implemented, it needs to cover more than parsing:

- IDL parsing
- JSON AST parsing
- prelude resolution
- imports
- projections/transforms
- trait validation
- semantic model validation
- compatibility with Smithy CLI behavior

---

## Build Pipeline

```text
User project
  |
  | <SmithyModel Include="models/**/*.smithy" />
  | optional smithy-build.json
  v
MSBuild target before CoreCompile
  |
  | invoke Smithy CLI / smithy-build
  | validate model and build selected projection
  v
Smithy JSON AST / build artifacts
  |
  | parse into Smithy.NET IR
  v
Generated C# under obj/<configuration>/<tfm>/Smithy/
  |
  | added to Compile items
  v
Roslyn compilation
  |
  | SmithyNet.Generators emits serializer metadata/glue
  v
User assembly
```

---

## Package Structure

```text
SmithyNet.Core                    - runtime metadata, core abstractions, Document
SmithyNet.CodeGeneration          - Smithy AST/build output to C# generator
SmithyNet.MSBuild                 - MSBuild targets and tasks
SmithyNet.Generators              - Roslyn incremental generators
SmithyNet.Json                    - JSON codec
SmithyNet.Cbor                    - CBOR codec
SmithyNet.Xml                     - XML codec
SmithyNet.Http                    - HTTP transport
SmithyNet.Client                  - generated client runtime
SmithyNet.Server                  - server dispatcher runtime
SmithyNet.Server.AspNetCore       - ASP.NET Core integration
SmithyNet.Protocols.RpcV2Cbor     - Smithy RPC v2 CBOR protocol
SmithyNet.Protocols.SimpleRestJson - Alloy simpleRestJson protocol
SmithyNet.Protocols.AwsJson       - AWS JSON 1.0/1.1 protocols
SmithyNet.Protocols.RestJson      - AWS restJson1 protocol
SmithyNet.Protocols.RestXml       - AWS restXml protocol
SmithyNet.Auth                    - authentication abstractions
SmithyNet.Auth.SigV4              - AWS Signature V4
SmithyNet.FSharp                  - F# idiomatic wrappers
```

---

## Type Mappings

| Smithy Type | Recommended .NET Type | Notes |
|-------------|------------------------|-------|
| `blob` | `ReadOnlyMemory<byte>` | Accept `byte[]` ergonomics at construction boundaries. |
| `boolean` | `bool` | Nullable only when Smithy member semantics require it. |
| `string` | `string` | Use nullable annotations for optional members. |
| `byte` | `sbyte` | Smithy byte is signed. |
| `short` | `short` | |
| `integer` | `int` | |
| `long` | `long` | |
| `float` | `float` | Preserve Smithy special-value behavior per protocol. |
| `double` | `double` | Preserve Smithy special-value behavior per protocol. |
| `bigInteger` | `BigInteger` | `System.Numerics`. |
| `bigDecimal` | custom `BigDecimal` or string-backed decimal value | Do not map to `decimal` by default because Smithy `bigDecimal` is arbitrary precision. |
| `timestamp` | `DateTimeOffset` | Formatting is protocol/trait-specific. |
| `document` | `SmithyDocument` / `Document` | Avoid binding the core runtime to `JsonElement`. |
| `list` | `IReadOnlyList<T>` | Preserve sparse/non-sparse semantics. |
| `map` | `IReadOnlyDictionary<string, T>` | Smithy map keys are strings. Preserve sparse/non-sparse semantics. |
| `structure` | `record` or sealed class | Choose based on required/default/init ergonomics. |
| `union` | closed tagged type | Include unknown variant for forward compatibility. |
| `enum` | string-backed custom type | Avoid losing unknown enum values. |
| `intEnum` | C# `enum` or int-backed custom type | Decide based on unknown-value strategy. |

---

## Key Design Decisions

1. Generated code location:
   - Default to `obj/`.
   - Provide opt-in emitted generated files for debugging or committed output.

2. Smithy CLI environment:
   - Do not bundle or download the Smithy CLI from the MSBuild package.
   - Prefer a managed project environment, such as Pixi with conda-forge `smithy-cli`.
   - Resolve `smithy` from `PATH` by default.
   - Allow explicit `SmithyCliPath` for builds that need a fixed executable path.

3. Source generator responsibility:
   - Use it for Roslyn-native work.
   - Keep model parsing and main type generation in MSBuild/codegen so external files, projections, and multiple generated files are handled predictably.

4. First protocols:
   - Keep `aws.protocols#restJson1` as an AWS-compatible generated client target.
   - Add `alloy#simpleRestJson` as the general-purpose HTTP/JSON target for generated clients and servers.
   - Smithy RPC v2 CBOR is a strong later target once the HTTP/JSON client/server path is stable.

5. Server before more AWS protocol coverage:
   - After the first generated client protocols work, prioritize a `simpleRestJson` generated server path and end-to-end example before expanding AWS-flavored protocol coverage.
   - This proves the core architecture with a full Smithy model to client/server round trip.

6. F# support:
   - Defer until the C# model/client path is stable.
   - Start with library helpers before generated F# source.

---

## Open Questions

1. Should the first public preview target `net10.0` only, or `net8.0;net10.0`?

2. How much HTTP/JSON protocol compliance is required before declaring `simpleRestJson` server support?

3. Should generated structure types prefer records everywhere, or sealed classes where constructor/default/nullability semantics are easier to control?

4. Should string enums be generated as custom value types from the start to preserve unknown values?

---

## References

- Smithy model building uses `smithy-build.json`, sources, imports, projections, plugins, and Maven dependencies.
- Protocol traits declare the traits a protocol implementation must understand.
- Smithy provides HTTP protocol compliance tests; use these as package acceptance tests for protocol support.
- .NET 10 is an LTS release supported for three years. .NET 8 support ends November 10, 2026; .NET 9 is an STS release and should not be a long-term target.
