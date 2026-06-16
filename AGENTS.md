# AGENTS.md

Guidance for AI coding agents working in **smithy-dotnet** (the NSmithy project).
Human-facing docs live at <https://thomaslaich.github.io/smithy-dotnet/> and in
`designs/`; this file is the short, operational version.

## What this repo is

NSmithy turns a [Smithy](https://smithy.io) model into idiomatic C# at build
time — typed clients, ASP.NET Core minimal-API server stubs, and shared model
types, generated as part of `dotnet build`. No separate codegen step and no JRE
required by consumers.

The build itself is **polyglot**:

- The code generator is **Java/Gradle** (`codegen/`), packaged as a Smithy
  `SmithyBuildPlugin` JAR.
- The runtime, MSBuild integration, and tests are **C#/.NET 10** (`NSmithy.slnx`).
- `NSmithy.MSBuild` invokes the bundled Smithy CLI during `dotnet build`, which
  loads the codegen JAR to emit C#.

## Common commands

This repo is driven by [`just`](https://github.com/casey/just); run `just` to
list recipes. Key ones:

| Command | What it does |
| --- | --- |
| `just build` | `codegen` + `restore` + `dotnet build` (Release). Run this first. |
| `just test` | `gradle test` (codegen) then `dotnet test` (runtime + conformance). |
| `just codegen` | Build the codegen JARs and publish to the local Maven cache (`~/.m2`). |
| `just fmt` / `just check-format` | Run / verify `treefmt` formatting. |
| `just pack` | Pack NuGet packages to `artifacts/packages` (used by examples). |
| `just refresh-examples` | Re-restore/rebuild the `examples/` against freshly packed packages. |
| `just ci` | `check-format build test pack` — the full CI pipeline locally. |
| `just docs` | Install and run the docs site (`website/`) dev server. |

You generally **must `just codegen` (or `just build`) before `dotnet test`**:
the conformance projects resolve
`io.github.thomaslaich.nsmithy:smithy-csharp-codegen:<version>-SNAPSHOT` from
`~/.m2`, so a stale or missing JAR produces confusing codegen errors.

## Repository layout

- `codegen/` — Java/Gradle code generators. `smithy-csharp-codegen` (C# emission)
  and `smithy-proto-codegen` (`.proto` emission for gRPC).
- `packages/` — C# runtime and tooling packages (`NSmithy.Core`, `NSmithy.Http`,
  `NSmithy.Client`, `NSmithy.Server.AspNetCore`, `NSmithy.Codecs.*`,
  `NSmithy.Protocols.*`, `NSmithy.MSBuild`).
- `tests/` — `NSmithy.Tests` (unit) and `tests/Conformance/*` (one project per
  protocol, run against the official Smithy/AWS protocol-test fixtures).
- `examples/` — runnable end-to-end samples (`simple-rest-json`, `rest-json1`,
  `rpcv2cbor`, `aws`, `grpc`, `polyglot`). These consume **packed** packages from
  `artifacts/packages`, not project references — see the gotcha below.
- `templates/NSmithy.Templates` — `dotnet new` project templates.
- `website/` — Astro/Starlight documentation site.
- `designs/` — design docs and architecture rationale.
- `justfile` — task runner entry point.

## Conventions and gotchas

- **Versioning is tag-driven.** Local builds use the `VersionPrefix` /
  `VersionSuffix` in `Directory.Build.props` (currently `0.2.0` + `SNAPSHOT`); the
  release workflow overrides the version from the GitHub release tag. When bumping
  the version, update `Directory.Build.props`, `codegen/build.gradle.kts`, the
  `NSmithy.MSBuild` targets, templates, examples, conformance `smithy-build.json`,
  and the website docs together. Do **not** touch the unrelated `path-data-parser`
  entry in `website/package-lock.json`.
- **Warnings are errors.** `Directory.Build.props` sets
  `TreatWarningsAsErrors=true` with `Recommended` analysis; keep the build clean.
- **Formatting is enforced** via `treefmt` (csharpier for C#, etc.). Run
  `just fmt` before committing; CI runs `just check-format`.
- **Examples build against packed packages.** After changing runtime code, run
  `just pack` (and often `just refresh-examples`) before example builds reflect
  the change — a plain `dotnet build` of an example will use the old `.nupkg`.
- **gRPC examples need two build passes** (the first emits the `.proto`, the
  second compiles it); `just refresh-examples` handles this.
- **Conformance numbers** on the docs Protocol Status page come from the
  `ConformanceRateTests.ReportConformanceRate` test output in each
  `tests/Conformance/*` project; regenerate them from there rather than editing by
  hand.

## Commit / PR conventions

- Branch off `main`; releases use `release/<x.y.z>` branches.
- Commit messages follow Conventional Commits (`feat:`, `fix:`, `chore:`,
  `docs:`, `refactor:`). PRs are squash-merged with a `(#NN)` suffix.
