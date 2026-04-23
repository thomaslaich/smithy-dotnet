# Known Limitations

Smithy.NET is still a preview-stage implementation. This page tracks the main
current limitations and rough edges rather than every missing feature.

## Smithy CLI And Build Environment

`SmithyNet.MSBuild` invokes an existing `smithy` executable. Smithy.NET does
not download, bundle, or pin the Smithy CLI. Build environments are expected to
provide it.

That means:

- builds depend on an external Smithy CLI installation
- Java may also be required, depending on the selected Smithy CLI distribution
- environment differences can show up as build differences if the CLI toolchain
  is not managed consistently

The recommended setup remains a managed project environment such as Pixi with
`smithy-cli` from conda-forge.

## Protocol Coverage Is Still Narrow

Current protocol support is intentionally selective:

- `alloy#simpleRestJson` is the most complete path and the best-covered
  end-to-end preview flow
- `aws.protocols#restJson1` client generation works, but covers a narrower
  slice and does not imply AWS-style server support
- `alloy.proto#grpc` is available through `.proto` generation and generated
  client/server adapters, but it is still the least mature path

Not yet implemented:

- Smithy RPC v2 CBOR
- REST XML
- AWS JSON protocols
- EC2 Query and AWS Query

`restJson1` server generation is not a current target.

## gRPC Is Experimental

The gRPC path exists, but it should still be treated as an early adopter track.

Current constraints include:

- smaller test and example coverage than the HTTP/JSON paths
- more explicit model requirements such as `alloy.proto#protoIndex`
- more implementation details that are still expected to move

## Server Support Is Narrow

Server support is currently centered on generated ASP.NET Core endpoints for
`alloy#simpleRestJson`.

Current limitations include:

- no general non-ASP.NET server story
- no broad server protocol matrix across multiple Smithy protocols
- response binding and error behavior that still need broader conformance
  coverage

## Validation Is Incomplete

Generated constructors do not implement full Smithy validation semantics.
Generated C# nullability is authoritative, but external request binding and
deserialization still need more protocol-aware runtime validation.

## Codec And Serialization Boundaries Are Still Maturing

`SmithyNet.Json` is reflection-based and intentionally small. It is not yet
optimized for:

- NativeAOT
- source-generated serializer metadata
- every Smithy edge case across future protocol families

This matters beyond JSON specifically: the project still needs more protocol
pressure from areas such as `rpcv2Cbor` and `restXml` to fully validate its
codec and transport abstractions.

## Client And Server Generation Are Still Coupled In Places

For `alloy#simpleRestJson`, the current generator still emits both client and
server surfaces for service shapes. Client-only projects that generate from
`simpleRestJson` services currently need server runtime references as well.

This is a known design debt and should be split into clearer generation modes.

## Generated Model Scope Can Be Too Broad By Default

By default, the generator emits all supported shapes in the assembled model.
When using Smithy build dependencies for traits or shared model packages,
configure `SmithyGeneratedNamespaces` so dependency model shapes are not emitted
as C#.

## Architecture Boundary Still Carries Cost

The current architecture keeps Smithy as the model front end and .NET as the
main backend generator. That is working, but it also means Smithy.NET owns:

- a Smithy JSON AST reader
- its own internal model representation
- a translation boundary between Smithy build output and generated C#

This is a deliberate current tradeoff, not an accident. Smithy.NET may still
experiment with moving selected parts of code generation into a Smithy Java
plugin if that looks likely to simplify semantic-model handling without giving
up the MSBuild-first workflow.
