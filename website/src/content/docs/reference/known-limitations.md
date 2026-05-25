---
title: Known Limitations
description: Current limitations and rough edges in the NSmithy preview.
---

NSmithy is still a preview-stage implementation.

## Smithy CLI And Build Environment

`NSmithy.MSBuild` invokes an existing `smithy` executable. NSmithy does
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
  end-to-end preview flow; the current pinned official request/response corpus
  passes at `43/43`
- `aws.protocols#restJson1` client and ASP.NET Core server generation work, and
  the current pinned official request/response corpus passes at `268/272`
- `alloy.proto#grpc` is available through `.proto` generation and generated
  client/server adapters, but it is still the least mature path

Not yet implemented:

- AWS JSON protocols
- EC2 Query and AWS Query

For `restJson1`, the remaining gap is not one single kind of missing feature.
The current uncovered slice is the Glacier-specific fixture set, which still
needs broader projection support.

## gRPC Is Experimental

The gRPC path exists, but it should still be treated as an early adopter track.

Current constraints include:

- smaller test and example coverage than the HTTP/JSON paths
- more explicit model requirements such as `alloy.proto#protoIndex`
- more implementation details that are still expected to move

## Server Support Is Narrow

Server support is currently centered on generated ASP.NET Core endpoints for
`alloy#simpleRestJson` and `aws.protocols#restJson1`.

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

`NSmithy.Codecs.Json` is reflection-based and intentionally small. It is not yet
optimized for:

- NativeAOT
- source-generated serializer metadata
- every Smithy edge case across future protocol families

Broader protocol coverage (`rpcv2Cbor`, `restXml`) will drive further validation of the codec and transport abstractions.

## Client And Server Generation Are Still Coupled In Places

For `alloy#simpleRestJson`, the current generator still emits both client and
server surfaces for service shapes. Client-only projects that generate from
`simpleRestJson` services currently need server runtime references as well.

This is a known design debt and should be split into clearer generation modes.

## Generated Model Scope Can Be Too Broad By Default

By default, the generator emits all supported shapes in the assembled model.
When using Smithy build dependencies for traits or shared model packages,
configure `SmithyBaseNamespace` so dependency model shapes are not emitted as C#.
