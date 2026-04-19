# Smithy.NET

Smithy.NET is an early-stage .NET toolkit for generating C# code and runtime clients from Smithy models.

Current status: C# model-shape generation and MSBuild integration are implemented for the first model-only slice. JSON codec and client generation are next on the roadmap. See `docs/planning/roadmap.md` for the implementation roadmap.

## MSBuild integration

The `Smithy.NET.MSBuild` package runs Smithy build before compilation, reads the selected projection's JSON AST, writes generated C# under `$(IntermediateOutputPath)Smithy/` by default, and adds those files to `Compile`.

Add Smithy model files to a project with:

```xml
<ItemGroup>
  <SmithyModel Include="models/**/*.smithy" />
</ItemGroup>
```

Optional MSBuild properties:

| Property | Default | Description |
| --- | --- | --- |
| `SmithyBuildFile` | `$(MSBuildProjectDirectory)/smithy-build.json` | Smithy build configuration file. If it is missing and `SmithyModel` items are present, a minimal build file is generated under `obj`. |
| `SmithyProjection` | `source` | Smithy build projection to compile. |
| `SmithyGeneratedOutputPath` | `$(IntermediateOutputPath)Smithy/` | Directory for generated `.g.cs` files that are added to `Compile`. |
| `SmithyBuildOutputPath` | `$(IntermediateOutputPath)SmithyBuild/` | Directory for Smithy build output and generated manifests. |
| `SmithyGeneratedFileManifest` | `$(SmithyBuildOutputPath)generated-files.json` | Manifest used to delete stale generated `.g.cs` files when shapes are renamed or removed. |
| `SmithyDependencyManifest` | `$(SmithyBuildOutputPath)dependencies.json` | Manifest recording the selected Smithy model output, configured model inputs, and Smithy source artifacts. |
| `SmithyDependencyInputFile` | `$(SmithyBuildOutputPath)dependency-inputs.txt` | Newline-delimited dependency file read by MSBuild incremental inputs on later builds. |
| `SmithyEmitGeneratedFiles` | `false` | Marks generated compile items as visible in IDE project views when set to `true`. |
| `SmithyCliPath` | empty | Explicit Smithy CLI executable path. When omitted, `smithy` is resolved from `PATH`. |

The current MSBuild task requires the Smithy CLI to be installed. If the CLI distribution uses Java, Java must also be available in the build environment. Bundled or pinned Smithy CLI acquisition is still planned.

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
