# NSmithy benchmark suite

A cross-stack performance comparison for `restJson1`, built so the numbers are
hard to dismiss.

The interesting part of this suite is not the numbers; it is the harness that
makes them comparable. Every stack under measurement is verified to serve
**byte-identical responses** to the same request corpus before any timing runs.
A performance comparison between stacks serving subtly different contracts is
not a comparison; the stack doing less work just wins.

## What is measured

The suites are grouped first by measurement boundary, then by protocol or codec.
The error suite runs with the JSON codecs because it contains no HTTP pipeline.

| Suite | What runs | Answers |
| --- | --- | --- |
| **codec** | Typed object ↔ bytes. No ASP.NET at all. | *What does serialization alone cost?* |
| **error** | A modelled error response vs the success response on the same operation. No ASP.NET. | *What does an error response cost beyond a success?* |
| **client** | Request building + response parsing, against a stub transport. | *What does one client call cost?* |
| **server** | Raw HTTP bytes in → bytes out, through an in-memory host. | *What does serving one request cost?* |

They are meant to be read together: the server suite says a stack is slower, the
codec suite says whether serialization explains it. That is how the serialization
work in `FINDINGS.md` went, and the codec numbers are what located it.

None of them use sockets. Loopback TCP and Kestrel connection handling would
dominate the measurement and add run-to-run variance unrelated to the code under
test. A socket-based macro suite answering "requests per second under load" is a
separate concern and is not built here.

The gRPC benchmark applies the same split to two unary operations: an NSmithy
client/server using the generated schema-driven Proto codec and a Grpc.Net
client/server using Google.Protobuf-generated messages. The committed `.proto`
is emitted from the same Smithy model, and parity tests require both stacks to
produce identical framed request and response bytes before they are timed.

## The servers

| Server | What it is | Role |
| --- | --- | --- |
| `nsmithy` | NSmithy's generated minimal-API server + schema codecs | Subject |
| `minimal-api` | Hand-written ASP.NET Core minimal API, `System.Text.Json` source-gen | Ceiling |
| `mvc` | Hand-written MVC controller, same DTOs and same source-gen context | Hosting-model control |

The MVC baseline exists to keep the third-party comparison honest. NSmithy
generates minimal-API endpoints; the TypeSpec and NSwag emitters generate MVC
controllers. Comparing those directly would fold two separate differences, hosting
model and codec, into one number. With a hand-written MVC baseline in
the set, the MVC-versus-minimal-API cost can be read off directly, and whatever
remains is attributable to the generated code.

"NSmithy reaches *n*% of hand-written" is a stronger and more useful claim than
"NSmithy beats generator X", so the baselines matter more than the competitors.

## The clients

| Client | What it is | Role |
| --- | --- | --- |
| `nsmithy` | NSmithy's generated client | Subject |
| `hand-written` | `HttpClient` + `System.Text.Json` source-gen | Ceiling |
| `nswag` | NSwag generated from the emitted OpenAPI, `System.Text.Json` mode | Third party |

Client benchmarks run against a stub `HttpMessageHandler`, not a server. A real
server contributes a large shared constant that compresses every ratio and adds
variance nobody controls. The canned response bytes are captured from the
reference server during setup, so they are real responses that simply are not
paid for on every iteration.

The stub deliberately reads each request body to completion. Content is normally
serialized during send, so a client that deferred serialization would otherwise
never pay for it.

## The parity gate

Nothing is timed until every stack is known to do the same work, in both
directions. `just bench-parity` runs it.

**Servers are pinned by the responses they return.** `contract/golden/` holds one
committed capture per scenario, recorded from the reference stack: status,
contract-relevant headers, body length, and body hash. Every server must
reproduce them exactly. Transport headers (`Date`, `Server`, `Content-Length`,
framing) are excluded, since they vary by host and by run without saying anything
about the contract. Bodies over 8 KB are stored as a hash plus a length rather
than inline, so the golden files stay reviewable in a diff; the comparison is
exact either way.

**Clients are pinned by the requests they emit**: method, path, query ordering,
headers, and body bytes, plus an assertion that each parsed the response into the
same values. Without it, "client A is faster" could just mean client A omits a
header. Every client runs against the same reference server, so the server is a
constant and cannot bias the comparison. Headers that cannot match by
construction are excluded, since `traceparent` and `tracestate` carry a fresh
trace id per call.

The gate earned its place immediately: on its first run it caught the
hand-written client omitting the `Accept: application/json` header that NSmithy's
client sends.

If the contract changes on purpose, re-record with `just bench-capture` and
review the diff. That is the point of committing the captures: a contract change
should be a reviewable event, not a silently moved goalpost.

## The contract

`contract/model/bench.smithy` is the single source of truth. It is emitted to
OpenAPI 3 as part of building the NSmithy stack, and that OpenAPI document is
what any third-party stack is generated from, and that is what guarantees the wire
contracts match rather than merely resembling each other.

Each operation isolates one cost centre:

| Operation | Isolates |
| --- | --- |
| `GetItem` | Fixed per-request overhead: one path label in, four scalars out |
| `SearchItems` | HTTP binding: 6 query parameters, 4 request headers, 1 response header |
| `ListItems` | Response body scaling, driven at 1 / 100 / 10 000 elements |
| `CreateOrder` | Large nested request body, driven at ~1 KB and ~1 MB |
| `GetItem` (miss) | Modeled error path: status code plus error discriminator |
| validation | Rejection path: `@range`, `@pattern`, `@length`, and multi-error aggregation |

**Constraint traits are present and enforced.** NSmithy derives validation from
`@pattern` / `@length` / `@range` in the model, so the hand-written baselines
validate by hand to match; a baseline that skipped it would be doing strictly less
work on every request. The constraints are chosen so every success scenario
passes them, and only the dedicated validation scenarios violate one.

## Running it

Requires `just codegen` first, like the conformance projects.

```sh
just bench-parity     # verify every server and client agrees, byte for byte
just bench-codec      # codec only, no ASP.NET
just bench-client     # client request building and response parsing
just bench-server     # full server path
just bench-grpc       # unary gRPC plus isolated Proto codec attribution
just bench            # all of the above
```

Each aggregate has protocol-specific recipes such as `bench-codec-cbor`,
`bench-client-grpc`, and `bench-server-rest-json`. Committed reports follow the
same layout under `results/{client,codec,server}/<protocol-or-codec>/`.

The benchmark suite is a separate solution and is intentionally **not** part of
`just build`, `just test`, or `just ci`. It is slow, and machine-dependent
numbers do not belong in a pass/fail pipeline. `just bench-parity` is the
exception worth wiring into CI eventually: it is fast, and it is what keeps the
comparison meaningful.

## Fairness rules

These are what the suite is actually built around. Breaking one invalidates the
numbers:

1. **Identical bytes on the wire.** Enforced by the parity gate, not by
   inspection.
2. **Identical business logic.** Every stack calls the same `Bench.Domain`
   methods. If each stack shipped its own handler, a difference in the handler
   would show up as a difference in the framework.
3. **Mapping stays in the measurement.** Stacks map domain records into their own
   generated DTOs. That is work a real user does, it is the same work for every
   stack, and engineering it away would flatter whichever stack has the most
   awkward types.
4. **No stack gets a tuned host.** Logging is off everywhere, GC mode is pinned
   in the benchmark project, and every stack runs on the same in-memory transport.

## Findings

`FINDINGS.md` records what the suite has surfaced: open performance problems,
what was fixed, correctness gaps, and what the suite still cannot answer. Every
item is tagged `MEASURED`, `OBSERVED`, or `HYPOTHESIS`, because reading code
produced two confident predictions during this work that measurement then
contradicted.

## Interpreting results

Allocation figures are deterministic and trustworthy even from a short run.
Timing figures are not: check BenchmarkDotNet's `Error` column before quoting a
mean. A short-job run can easily report an error margin larger than the
difference being claimed.
