# Methodology

How these numbers are produced, and what has to be true for them to mean
anything. Written so a result can be reproduced or challenged rather than taken
on trust.

## Preconditions

A run is only valid if all of these hold:

1. `just bench-parity` passes. Every stack serves byte-identical responses to
   every corpus scenario. Without this the comparison is meaningless.
2. The build is `Release`.
3. Nothing else meaningful is running on the machine. These are microbenchmarks;
   a background build will show up in the results.

## What is held constant

| Knob | Setting | Why |
| --- | --- | --- |
| Target framework | `net10.0` | Same for every stack. |
| GC | Workstation, non-concurrent | Pinned in `Benchmarks.Micro/Bench.Micro.csproj`. Low variance and clean attribution. Server GC is what production uses and is the right choice for a future socket-based macro suite, but it is not what these levels are for. |
| `TieredPGO` | off | Removes a source of run-to-run drift in steady-state measurement. |
| Logging | disabled in every host | Its cost differs by stack, which would read as a framework difference that is not one. |
| Transport | in-memory `TestServer` | No sockets, no ports, no Kestrel. |
| Business logic | shared `Bench.Domain` | Identical across stacks by construction. |
| Toolchain | `--inProcess` | Required on this repo's Nix setup, see below. |

## The `--inProcess` toolchain

The `just bench-*` recipes pass `--inProcess`, and that is not optional here.

BenchmarkDotNet's default toolchain generates and builds a throwaway project per
run. On this repo's Nix/devenv setup, where `dotnet` is a Nix-wrapped SDK, that
step resolves an SDK root inside `/nix/store` and recursively enumerates it. It
never finishes. On a hung run the main thread sits in `Monitor_Wait` while a
worker burns samples in `OpenDir`/`ReadDir`/`LStat`, with 135 open handles under
`/nix/store`. `--buildTimeout` does not help: the walk happens before the build,
where no timeout applies. `--inProcess` skips project generation altogether and
takes the suite from hanging indefinitely to seconds.

The exact BenchmarkDotNet code path that computes that root has not been pinned
down; only the symptom and the workaround are established.

Two things this does **not** change:

- **GC settings still apply.** They are pinned in `Benchmarks.Micro/Bench.Micro.csproj`,
  and with `--inProcess` the host process is the one running the benchmarks.
- **Results are not distorted.** Cross-checked at the switchover: the
  deserialization benchmarks, which no code change touched, moved from 1.37x/1.52x
  out-of-process to 1.35x/1.45x in-process, with identical allocation ratios
  (1.29x / 1.17x). That is within run-to-run noise.

Comparing a number measured in-process against one measured out-of-process is
still not something to do casually, absolute figures shift, because cold-start
and harness overhead differ. Ratios within a single table remain the safe unit.

## What is deliberately *not* controlled

- **CPU pinning / frequency scaling.** Not done. Results from a laptop under
  thermal pressure are directional at best.
- **Cross-machine comparability.** Numbers from different machines are not
  comparable. Only compare rows within a single run's table.

## Reading the output

**Allocations are the reliable signal.** `Allocated` and the `Gen0`/`Gen1`/`Gen2`
columns are deterministic, they do not depend on machine load and are
trustworthy even from a short run. An allocation difference is a real finding.

**Timings need scrutiny.** Check the `Error` column (half of the 99.9% confidence
interval) before quoting a mean. A `--job short` run routinely produces error
margins larger than the difference being claimed; such a result supports no
conclusion at all. Prefer `Median` over `Mean` when the two diverge, and re-run
with the default job before believing a timing delta.

**Ratios only within a table.** The codec suite reports a `Ratio` column against the
`System.Text.Json` source-generated baseline. That number is meaningful. Ratios
computed by hand across separate runs are not.

## Recording a run

Results land under `benchmarks/results/`, grouped by measurement boundary
(`client`, `codec`, or `server`) and then protocol or codec. BenchmarkDotNet
writes the full environment block, OS, CPU, SDK and runtime version, JIT, and job
configuration into every report. Keep that block with any number that gets
quoted; a benchmark figure without its environment is not a result.

## Known gaps

Stated here rather than discovered later:

- **No socket-level macro suite.** Requests per second, tail latency under
  concurrency, and RSS are not measured. Those need separate processes, a load
  generator, and a pinned machine.
- **One third-party stack, on one side.** NSwag is measured as a client, generated
  from the emitted OpenAPI. TypeSpec is wired up but excluded: its C# server
  emitter is alpha and cannot serve the contract, so no third-party server is
  compared at all.
- **No wide structures.** The widest shape in the model is six members, so nothing
  here shows how member lookup or the write path scale with structure width.
- **No end-to-end RPCv2 CBOR or XML comparison.** JSON and gRPC have client and
  server coverage. CBOR and XML currently have codec serialization coverage
  only.
