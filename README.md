# Smithy.NET

Smithy.NET is an early-stage .NET toolkit for generating C# models and typed
HTTP clients from Smithy models.

The current preview slice focuses on one working path:

- run the Smithy CLI from MSBuild
- read the selected Smithy build projection JSON AST
- generate C# model types into `obj/`
- generate a typed `restJson1` client
- serialize and deserialize JSON payloads through Smithy metadata

Server generation, bundled Smithy CLI acquisition, additional protocols, and
NativeAOT serializer generation are still planned work. See
[the roadmap](docs/planning/roadmap.md) for the implementation plan.

## Packages

The preview package set is:

| Package | Purpose |
| --- | --- |
| `SmithyNet.Core` | Shared runtime primitives, generated-code attributes, Smithy IDs, and document values. |
| `SmithyNet.CodeGeneration` | Smithy JSON AST reader and C# generator. |
| `SmithyNet.MSBuild` | MSBuild integration that invokes Smithy build and adds generated C# to compilation. |
| `SmithyNet.Json` | Reflection-based JSON serializer for generated Smithy shapes. |
| `SmithyNet.Http` | HTTP transport abstractions and `HttpClient` transport. |
| `SmithyNet.Client` | Operation invoker, client middleware pipeline, errors, and retry middleware. |
| `SmithyNet.Generators` | Placeholder for future Roslyn/source-generation work. |

## Quick Start

Create local packages:

```bash
dotnet pack SmithyNet.slnx --configuration Release --output artifacts/packages
```

Create a consumer project that references the local package source and add
`SmithyNet.MSBuild` plus the runtime packages needed by generated clients:

```xml
<ItemGroup>
  <PackageReference Include="SmithyNet.Client" Version="1.0.0" />
  <PackageReference Include="SmithyNet.Core" Version="1.0.0" />
  <PackageReference Include="SmithyNet.Http" Version="1.0.0" />
  <PackageReference Include="SmithyNet.Json" Version="1.0.0" />
  <PackageReference Include="SmithyNet.MSBuild" Version="1.0.0" PrivateAssets="all" />
</ItemGroup>
```

Add a Smithy model through either a `smithy-build.json` file or `SmithyModel`
items. A `smithy-build.json` is recommended when the model has imports, Maven
dependencies, or projections:

```json
{
  "version": "1.0",
  "sources": ["model"],
  "maven": {
    "dependencies": ["software.amazon.smithy:smithy-aws-traits:1.68.0"]
  }
}
```

Build the project. The MSBuild package runs `smithy build`, writes generated C#
under `$(IntermediateOutputPath)Smithy/`, and adds those files to `Compile`.

See [Quick Start](docs/quick-start.md) for a complete example project.

## Documentation

- [Quick Start](docs/quick-start.md)
- [MSBuild Reference](docs/msbuild.md)
- [Supported Surface](docs/supported-surface.md)
- [Known Limitations](docs/known-limitations.md)
- [Polyglot Example](examples/polyglot/README.md)
- [Roadmap](docs/planning/roadmap.md)

## Development

This repository uses Nix and devenv as the supported development environment.

Prerequisites:

- Install Nix. Determinate Nix is recommended.
- Install devenv: `nix profile add nixpkgs#devenv`

Enter the shell:

```bash
devenv shell
```

Common commands:

```bash
just restore       # dotnet restore
just fmt           # format the repo with treefmt
just check-format  # verify treefmt formatting
just build         # release build
just test          # release tests
just pack          # create NuGet packages under artifacts/packages
just ci            # restore, format check, build, test, pack
```
