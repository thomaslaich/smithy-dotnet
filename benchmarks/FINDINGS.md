# Findings

Open problems the suite has surfaced. Numbers come from the committed reports
under `results/` (all from 2026-11-14), Tiered PGO off, ratios within one
report. Fixed items are in git
history: `git log --grep '^perf'`.

## Open

### JSON serialization is 1.06 to 1.18× STJ source-gen

`SerializationBenchmarks`: 1.18× for one item, 1.14× for 100, 1.06× for
10 000, while allocating 0.82 to 0.87× as much. `SerializationExecutionBenchmarks`
reuses the writer and buffer and lands at 0.97 to 1.04×, so most of the gap is
writer setup and the copy into the returned array. The remaining 4 % is
generic virtual dispatch through `Schema<T>.Write`, `SerializeMembers` and
`SerializeElements`, about four calls per item. Removing it would need
generated schemas that call each other directly instead of through the schema
base types.

### The client pays a fixed per-call cost

`ClientCeremonyBenchmarks` isolates it: 1.38 µs and 4.69 KB against 0.92 µs
and 2.66 KB hand-written. In `ClientBenchmarks` the time ratio decays with
payload size, 1.52× on `get-item`, 0.85× on `list-items-100`, 0.67× on
`list-items-10000`, and against NSwag 1.33× to 0.84×. That is a constant being
amortized, not a codec cost. The constant is the public runtime, protocol and
transport abstractions plus the HTTP request and response objects. The
hand-written client has no retry, interceptors, telemetry or auth, so it is a
ceiling rather than a feature-equivalent comparison.

Allocations do not decay. On `list-items-10000` NSmithy allocates 6,239 KB
against NSwag's 3,929 KB. `ListItemsAttributionBenchmarks` splits it: the codec
alone allocates less than STJ source-gen on the same body (4,786 KB against
5,489 KB), the client adds the 1.45 MB body array, and the immutable generated
model plus collection wrappers allocate more than NSwag's whole call into
mutable DTOs. Closing it needs a pooled body buffer and a span-based codec API.

### HTTP binding on the server

`ServerBenchmarks` `search-items`: 17.6 µs and 19.7 KB against 10.8 µs and
14.1 KB for the minimal API, a 1.6× gap. `get-item-hit` is 1.18× and
`list-items-100` 0.84×, so the body is not the difference. The scenario's six
query parameters, four request headers and one response header are the likely
cost. Not yet attributed.

### A modeled error costs an exception

`get-item-miss` is 17.0 µs against 6.4 µs in the report. Measured awaited, as
Kestrel runs requests, the gap is about 2×, of which about 2 µs is the throw and
catch. The exception object and message cost nothing measurable. Removing the
throw needs a way for a handler to return a modeled error instead of raising it.

### The TypeSpec server costs 1.3× the hand-written MVC baseline on fixed overhead

`ServerBenchmarks`, on the four scenarios it serves. `get-item-hit` is 14.7 µs
and 14.0 KB against 11.3 µs and 13.0 KB for hand-written MVC and 7.4 µs and
11.4 KB for NSmithy. On the lists it is 1.13× MVC on `list-items-100` and
1.29× on `list-items-10000`, with allocations within 2 % of MVC. So it is
mostly a fixed per-request cost, about 3.5 µs over the same hosting model, not
a serialization cost. Not yet attributed. The
generated models route every constrained string through a `JsonConverter`
that runs `Regex.IsMatch` on write, and the host uses reflection-based
`System.Text.Json`, which are the two visible differences from the MVC
baseline. Against NSmithy the end-to-end gap is 2.0× on `get-item-hit` and
1.5× on the list scenarios.

### Proto codec against Google.Protobuf

Codec: serialize 1.11× and 1.01×, deserialize 1.34× and 1.47× for `get-item`
and `list-items-100`, allocations 0.58 to 1.12×. End to end: client 1.36× and
1.48×, server 1.06× and 1.01×, allocations 1.04 to 1.56×. What remains is
per-field schema dispatch and immutable model construction, the same shape as
the JSON gap.

### The XML codec builds a DOM and a string in each direction

Serialize goes `XElement`, `string`, bytes; deserialize goes bytes, `string`,
`XElement`. One item costs 1.5 µs and 15.7 KB, 100 items 69 µs and 186 KB,
against 212 ns and 216 B or 16.2 µs and 14.6 KB for JSON. `XmlWriter` and
`XmlReader` over a pooled buffer would remove the DOM and both string copies.

### CBOR is at its allocation floor but 1.5 to 1.7× JSON's time

144 B for one item and 11.6 KB for 100, which is the returned array. Time is
343 ns and 25.2 µs. Pre-encoding member names was tried and was slower, because
`WriteEncodedValue` validates its input.

### The hand-written client is not a ceiling on `list-items-10000`

It allocates 5,491 KB and takes 7.16 ms where NSwag allocates 3,929 KB and
takes 5.68 ms. The remaining differences are `ReadFromJsonAsync` against an
explicit stream plus `DeserializeAsync`, and the DTO collection types. Until
that is explained, do not quote "n % of hand-written" for this scenario.

## Gaps

- No socket-level suite. Throughput, tail latency under concurrency and RSS are
  not measured.
- The TypeSpec server is compared on `get-item-hit` and the three `list-items`
  sizes only. Its emitter (0.58.0-alpha.33) puts response headers and status
  codes into the body and does not enforce query constraints, so the other
  eight scenarios are unserved. NSwag clients discard response headers; the
  suite reads `x-total-count` through its `ProcessResponse` hook.
- CBOR and XML have codec coverage only, no client or server suite.
- No wide structures. The widest shape has six members, so the linear member
  scan on the read path is never stressed.
- No `@default` members, so default materialization never runs in any suite.
- The client's fixed cost is not attributed per optional feature (retry,
  interceptors, auth, endpoint resolution, telemetry).
