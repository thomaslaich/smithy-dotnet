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
| `just codegen` | Build the codegen JARs and stage the bundled Maven repo used by `NSmithy.MSBuild`. |
| `just fmt` / `just check-format` | Run / verify `treefmt` formatting. |
| `just pack` | Pack NuGet packages to `artifacts/packages` (used by examples). |
| `just refresh-examples` | Re-restore/rebuild the `examples/` against freshly packed packages. |
| `just ci` | `check-format build test pack` — the full CI pipeline locally. |
| `just docs` | Install and run the docs site (`website/`) dev server. |

You generally **must `just codegen` (or `just build`) before `dotnet test`**:
the conformance projects resolve
`io.github.thomaslaich.nsmithy:smithy-csharp-codegen:<version>-SNAPSHOT` from
the bundled Maven repo under `packages/NSmithy.MSBuild/tools/maven-repo`, so a
stale or missing staged JAR produces confusing codegen errors.

## Repository layout

- `codegen/` — Java/Gradle code generators. `smithy-csharp-codegen` (C# emission)
  and `smithy-proto-codegen` (`.proto` emission for gRPC).
- `packages/` — C# runtime and tooling packages (`NSmithy.Core`, `NSmithy.Http`,
  `NSmithy.Client`, `NSmithy.Server.AspNetCore`, `NSmithy.Codecs.*`,
  `NSmithy.Protocols.*`, `NSmithy.MSBuild`).
- `tests/` — `NSmithy.Tests` (unit) and `tests/Conformance/*` (one project per
  protocol, run against the official Smithy/AWS protocol-test fixtures).
- `examples/` — runnable end-to-end samples (`simple-rest-json`, `rest-json1`,
  `rpcv2cbor`, `aws-localstack`, `grpc`, `grpc-streaming`, `polyglot`). These
  consume **packed** packages from `artifacts/packages`, not project references —
  see the gotcha below.
- `templates/NSmithy.Templates` — `dotnet new` project templates.
- `website/` — Astro/Starlight documentation site.
- `designs/` — design docs and architecture rationale.
- `justfile` — task runner entry point.

## Conventions and gotchas

- **Versioning is tag-driven.** Local builds use the dev placeholder
  `0.0.0-SNAPSHOT` from `Directory.Build.props` and the default Gradle version;
  the user-facing version lives in the repo-root `VERSION` file, and release
  builds override .NET/Gradle versions from the GitHub release tag. When preparing
  a release, update `VERSION` and any docs/templates/examples that intentionally
  mention the released version together. Keep the dev placeholders in
  `Directory.Build.props`, `codegen/build.gradle.kts`,
  `NSmithy.MSBuild` targets, examples, and conformance `smithy-build.json`
  aligned at `0.0.0-SNAPSHOT`.
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
