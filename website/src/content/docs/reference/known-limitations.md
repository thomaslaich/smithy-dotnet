---
title: Known limitations
description: Current restrictions in the NSmithy preview.
---

NSmithy is a preview implementation. [Protocol status](/smithy-dotnet/protocols/status/)
is the authoritative coverage matrix; passing protocol tests does not establish
compatibility with every service or model.

## Protocols and streaming

AWS JSON, AWS Query, EC2 Query, and REST XML generate clients only.
gRPC is experimental and requires protobuf field indices in the model.

Streaming support depends on the protocol and shape. `restJson1` includes streaming
blobs and event streams; RPC v2 CBOR and gRPC include event streams. Consult the
individual [protocol pages](/smithy-dotnet/protocols/overview/) for supported
directions, payloads, and restrictions.

ASP.NET Core hosts the supported wire protocols. The separate
[MCP adapter](/smithy-dotnet/servers/mcp/) exposes modeled tools and prompts;
it does not infer MCP resources from Smithy resources.

## Validation

The server does not validate individual stream events or enforce `@length` on
unread streaming blobs. Traits outside the implemented constraint set, such as
`@idRef`, are not enforced. See [Validation](/smithy-dotnet/servers/validation/).

## NativeAOT

Codecs use generated schema accessors without runtime reflection. CI publishes
and runs a NativeAOT smoke test for REST JSON with label, header, query, and
body bindings. This does not cover every codec and protocol combination.

## Model dependencies

Common Smithy and alloy dependencies are bundled. Additional Maven artifacts
must be available through the project's configured repositories.

NuGet contract packages require explicit model and build-file imports. A package
reference alone does not trigger generation. Build-file synthesis accepts one
contract configuration. See [Distributing contracts](/smithy-dotnet/guides/distributing-contracts/)
for the supported setup.
