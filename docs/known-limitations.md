# Known Limitations

Smithy.NET is still a preview-stage implementation.

## Package Versioning

Local packages currently use `0.1.0-preview.1`. When repacking during
development, NuGet may keep an older restored copy of the same version in a
consumer project's package cache. The .NET examples avoid the global cache with:

```xml
<RestorePackagesPath>$(MSBuildProjectDirectory)/obj/packages</RestorePackagesPath>
```

If a local consumer keeps using stale package contents or stale generated
source, clear that consumer's `obj` directory or use a new package version. For
the repository examples, run:

```bash
just refresh-examples
```

## Smithy CLI Environment

The MSBuild task invokes an existing `smithy` executable. Smithy.NET does not
download, pin, or bundle the Smithy CLI; build environments are expected to
provide it. The recommended setup is a managed project environment, such as Pixi
with `smithy-cli` from conda-forge. `SmithyCliPath` remains available for builds
that cannot rely on `PATH` or need to force a specific executable.

## Protocol Coverage

Only narrow HTTP/JSON protocol slices are implemented:

- generated clients for `aws.protocols#restJson1`
- generated clients for `alloy#simpleRestJson`
- generated ASP.NET Core servers for `alloy#simpleRestJson`

The project does not yet support:

- Smithy RPC v2 CBOR
- AWS JSON protocols
- REST XML
- EC2 Query or AWS Query
- `alloy#grpc` clients and servers

`restJson1` server generation is not planned — the protocol carries AWS-specific
authentication and error-envelope requirements that have no practical use case
outside of mocking AWS services.

## Server Runtime

Server support is currently limited to generated ASP.NET Core endpoints for
`alloy#simpleRestJson`. Generated server surfaces include operation-scoped
handler interfaces, an aggregate service handler interface, generated service
and operation descriptors, and a DI helper for single-class handlers.

Non-ASP.NET server adapters and `alloy#grpc` support are not part of this
preview.

## Validation

Generated constructors do not implement full Smithy validation. Generated C#
nullability is authoritative, but external request binding and deserialization
still need protocol-aware runtime validation.

## JSON Runtime

The JSON implementation is reflection-based and intentionally small. It is not
yet optimized for NativeAOT, source-generated metadata, or every Smithy edge
case.

## Server And Client Generation Coupling

For `alloy#simpleRestJson`, the current generator emits both client and server
surfaces for service shapes. Client-only projects that generate from
`simpleRestJson` services currently need the server runtime references as well.
This is expected to be split into more explicit generation modes later.

## Model Scope

By default, the generator emits all supported shapes in the assembled model.
When using Smithy build dependencies for traits, configure
`SmithyGeneratedNamespaces` so dependency model shapes are not emitted as C#.
