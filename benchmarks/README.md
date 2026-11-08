# Benchmarks

Cross-stack microbenchmarks for NSmithy's `restJson1` and gRPC paths. Every
stack is checked to put byte-identical bytes on the wire before anything is
timed. `FINDINGS.md` lists what is open.

## Suites

| Suite | Measures | Transport |
| --- | --- | --- |
| codec | Typed object to bytes and back. Also a modeled error against a success on the same operation. | None |
| client | Request building and response parsing. | Stub `HttpMessageHandler` replaying responses captured from the reference server |
| server | HTTP bytes in, bytes out. | In-memory `TestServer` |

No sockets anywhere. Throughput and tail latency under load are not measured.

## Stacks

| Stack | What it is | Role |
| --- | --- | --- |
| `nsmithy` server | Generated minimal API and schema codecs | Subject |
| `minimal-api` | Hand-written minimal API, `System.Text.Json` source-gen | Ceiling |
| `mvc` | Hand-written controller, same DTOs | Hosting-model control. Third-party emitters generate MVC controllers, and MVC alone costs 1.5× or more on this suite. |
| `nsmithy` client | Generated client | Subject |
| `hand-written` | `HttpClient` and `System.Text.Json` source-gen | Ceiling |
| `nswag` | Generated from the emitted OpenAPI | Third party |
| `typespec` | MVC controllers from `@typespec/http-server-csharp` 0.58.0-alpha.33 | Third party. Serves 4 of the 12 scenarios, see `stacks/TypeSpec/README.md`. |
| gRPC | NSmithy client and server with the schema-driven Proto codec against Grpc.Net with Google.Protobuf messages | Subject and ceiling |

## Contract

`contract/model/bench.smithy` is the source. The OpenAPI document NSwag is
generated from and the `.proto` Grpc.Net uses are both emitted from it.

| Operation | Isolates |
| --- | --- |
| `GetItem` | Fixed per-request cost: one path label in, four scalars out |
| `SearchItems` | HTTP binding: 6 query parameters, 4 request headers, 1 response header |
| `ListItems` | Response body scaling at 1, 100 and 10 000 items |
| `CreateOrder` | Nested request body at about 1 KB and 1 MB |
| `GetItem` miss | Modeled error path |
| validation | Rejection path: `@range`, `@pattern`, `@length`, multi-error aggregation |

Constraint traits are enforced in every stack. The hand-written baselines
validate by hand so they do the same work.

## Rules

1. **Identical bytes.** `just bench-parity` checks every server against the
   golden captures in `contract/golden/` (status, contract headers, body length
   and hash) and every client against the same reference server (method, path,
   query, headers, body, parsed values). Transport headers and trace ids are
   excluded, and `Content-Type` is compared by media type only. A stack may
   declare scenarios it cannot serve: the gate checks that each one really
   differs, and the server suite skips them, so the stack is compared on the
   rest. When the contract changes on purpose, re-record with
   `just bench-capture` and review the diff.
2. **Shared business logic.** Every stack calls the same `Bench.Domain` methods.
3. **Mapping stays in.** Stacks map domain records into their own DTOs. Real
   users do that work too.
4. **Same host.** Logging off, workstation non-concurrent GC, `TieredPGO` off,
   pinned in `Benchmarks.Micro/Bench.Micro.csproj`.

## Running

Run `just codegen` first.

```sh
just bench-parity
just bench-codec     # or bench-codec-json | cbor | xml | proto
just bench-client    # or bench-client-rest-json | grpc
just bench-server    # or bench-server-rest-json | grpc
just bench-grpc
just bench           # everything
```

Reports land in `results/{codec,client,server}/<protocol>/` with the full
environment block. The suite is not part of `just build`, `just test` or
`just ci`.

Every recipe passes `--inProcess`. BenchmarkDotNet's default toolchain hangs on
this repo's Nix-wrapped SDK: it enumerates the SDK root under `/nix/store`
before the build step, where `--buildTimeout` does not apply. In-process
results matched out-of-process results within noise when the switch was made.

## Reading results

- Allocations are deterministic and reliable even from a short run.
- Check `Error` before quoting a mean, and prefer `Median` when the two
  diverge. A `--job short` run often has margins larger than the delta claimed.
- Compare rows within one report only. `create-order-large` and
  `list-items-10000` move 5 to 20 % between runs with no code change. Before
  believing a delta, check that the untouched stacks held still.
- `get-item-miss` overstates the modeled-error cost. BenchmarkDotNet blocks on
  the returned task, which makes a throw about 9 µs dearer than under an
  awaiting caller like Kestrel.
