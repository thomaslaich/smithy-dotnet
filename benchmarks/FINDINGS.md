# Findings

Open problems the suite has surfaced. Numbers come from the committed reports
under `results/` (REST JSON from 2026-10-28, gRPC, CBOR and XML from
2026-10-08), Tiered PGO off, ratios within one report. Fixed items are in git
history: `git log --grep '^perf'`.

## Open

### JSON serialization is 1.10 to 1.19× STJ source-gen

`SerializationBenchmarks`: 1.19× for one item, 1.14× for 100, 1.10× for
10 000, while allocating 0.82 to 0.87× as much. `SerializationExecutionBenchmarks`
reuses the writer and buffer and lands at 0.99 to 1.06×, so most of the gap is
writer setup and the copy into the returned array. The remaining 6 % is
generic virtual dispatch through `Schema<T>.Write`, `SerializeMembers` and
`SerializeElements`, about four calls per item. Removing it would need
generated schemas that call each other directly instead of through the schema
base types.

### The client pays a fixed per-call cost

`ClientCeremonyBenchmarks` isolates it: 1.58 µs and 4.75 KB against 1.00 µs
and 2.66 KB hand-written. In `ClientBenchmarks` the time ratio decays with
payload size, 1.53× on `get-item`, 0.82× on `list-items-100`, 0.67× on
`list-items-10000`, and against NSwag 1.35× to 0.85×. That is a constant being
amortized, not a codec cost. The constant is the public runtime, protocol and
transport abstractions plus the HTTP request and response objects. The
hand-written client has no retry, interceptors, telemetry or auth, so it is a
ceiling rather than a feature-equivalent comparison.

Allocations do not decay. On `list-items-10000` NSmithy allocates 6,161 KB
against NSwag's 3,930 KB. `ListItemsAttributionBenchmarks` splits it: the codec
alone allocates less than STJ source-gen on the same body (4,707 KB against
5,489 KB), the client adds the 1.45 MB body array, and the immutable generated
model plus collection wrappers allocate more than NSwag's whole call into
mutable DTOs. Closing it needs a pooled body buffer and a span-based codec API.

### HTTP binding on the server

`ServerBenchmarks` `search-items`: 18.3 µs and 22.5 KB against 9.15 µs and
14.1 KB for the minimal API, a 2.0× gap. `get-item-hit` is 1.25× and
`list-items-100` 0.95×, so the body is not the difference. The scenario's six
query parameters, four request headers and one response header are the likely
cost. Not yet attributed.

### A modeled error costs an exception

`get-item-miss` is 17.4 µs against 5.2 µs in the report. Measured awaited, as
Kestrel runs requests, the gap is about 2×, of which about 2 µs is the throw and
catch. The exception object and message cost nothing measurable. Removing the
throw needs a way for a handler to return a modeled error instead of raising it.

### Proto codec against Google.Protobuf

Codec: serialize 1.18× and 1.09×, deserialize 1.33× and 1.43× for `get-item`
and `list-items-100`, allocations 0.58 to 1.16×. End to end: client 1.36× and
1.48×, server 1.14× and 1.13×, allocations 1.19 to 1.57×. What remains is
per-field schema dispatch and immutable model construction, the same shape as
the JSON gap.

### The XML codec builds a DOM and a string in each direction

Serialize goes `XElement`, `string`, bytes; deserialize goes bytes, `string`,
`XElement`. One item costs 1.5 µs and 15.7 KB, 100 items 69 µs and 186 KB,
against 215 ns and 216 B or 16.8 µs and 14.6 KB for JSON. `XmlWriter` and
`XmlReader` over a pooled buffer would remove the DOM and both string copies.

### CBOR is at its allocation floor but 1.5 to 1.7× JSON's time

144 B for one item and 11.6 KB for 100, which is the returned array. Time is
357 ns and 25.9 µs. Pre-encoding member names was tried and was slower, because
`WriteEncodedValue` validates its input.

### The hand-written client is not a ceiling on `list-items-10000`

It allocates 5,491 KB and takes 6.99 ms where NSwag allocates 3,930 KB and
takes 5.51 ms. The remaining differences are `ReadFromJsonAsync` against an
explicit stream plus `DeserializeAsync`, and the DTO collection types. Until
that is explained, do not quote "n % of hand-written" for this scenario.

## Gaps

- No socket-level suite. Throughput, tail latency under concurrency and RSS are
  not measured.
- No third-party server. TypeSpec's C# server emitter (0.58.0-alpha) cannot
  serve the contract. NSwag clients discard response headers; the suite reads
  `x-total-count` through its `ProcessResponse` hook.
- CBOR and XML have codec coverage only, no client or server suite.
- No wide structures. The widest shape has six members, so the linear member
  scan on the read path is never stressed.
- No `@default` members, so default materialization never runs in any suite.
- The client's fixed cost is not attributed per optional feature (retry,
  interceptors, auth, endpoint resolution, telemetry).
