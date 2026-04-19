# Known Limitations

Smithy.NET is still a preview-stage implementation.

## Package Versioning

Local packages currently use `0.1.0-preview.1`. When repacking during development, NuGet
may keep an older restored copy of the same version in a consumer project's
package cache. The polyglot .NET example avoids the global cache with:

```xml
<RestorePackagesPath>$(MSBuildProjectDirectory)/obj/packages</RestorePackagesPath>
```

If a local consumer keeps using stale package contents, clear that consumer's
restored `SmithyNet.*` package folders or use a new package version.

## Smithy CLI Environment

The MSBuild task invokes an existing `smithy` executable. Smithy.NET does not
download, pin, or bundle the Smithy CLI; build environments are expected to
provide it. The recommended setup is a managed project environment, such as Pixi
with `smithy-cli` from conda-forge. `SmithyCliPath` remains available for builds
that cannot rely on `PATH` or need to force a specific executable.

## Protocol Coverage

Only a narrow generated-client `restJson1` path is implemented. The project does
not yet support:

- `alloy#simpleRestJson`
- Smithy RPC v2 CBOR
- AWS JSON protocols
- REST XML
- EC2 Query or AWS Query
- server-side protocol handling

## Server Runtime

There is no `SmithyNet.Server` or ASP.NET Core integration yet. Generated
clients can call compatible services, but Smithy.NET cannot yet generate server
handlers.

## Validation

Generated constructors do not implement full Smithy validation. Required input
members may still be nullable in the default generated mode. Generated clients
perform targeted checks where required for HTTP binding safety, such as
non-null HTTP labels.

## JSON Runtime

The JSON implementation is reflection-based and intentionally small. It is not
yet optimized for NativeAOT, source-generated metadata, or every Smithy edge
case.

## Model Scope

By default, the generator emits all supported shapes in the assembled model.
When using Smithy build dependencies for traits, configure
`SmithyGeneratedNamespaces` so dependency model shapes are not emitted as C#.
