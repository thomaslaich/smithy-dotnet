# Findings

What the benchmark suite has surfaced, and what is still unexplained.

## Provenance of the numbers

Every measurement here was taken on a tree with the sibling codec and conformance
changes applied. This branch adds only the suite, so running it here reproduces
the **pre-fix** numbers instead, serialization at 2.23–2.52× `System.Text.Json`
rather than 1.75–1.86×, deserialization at 1.38–1.44× rather than 1.14–1.15×, and
the uncorrected conformance rates.

Land this after those changes and the figures match what the suite reports. The
"Fixed" entries below likewise describe work that lives in those changes, not in
this one.

## How to read this

Every item is tagged with how strongly it is established. The distinction matters
,  during this work, reading code produced two confident predictions that
measurement then contradicted:

- The `MemoryStream` → pooled-buffer fix was predicted to buy time as well as
  allocations. It bought a **3.6× allocation reduction and no measurable time**.
- NSwag's reflection-based `System.Text.Json` was predicted to cost it against a
  source-generated baseline. It **matched the baseline and beat it** on large
  responses.

So: `MEASURED` means there are numbers with tight margins. `OBSERVED` means it is
visible in source or a captured artifact. `HYPOTHESIS` means it is reasoning that
has not been isolated by measurement, and should be treated as a lead, not a
conclusion.

---

## Open performance problems

### 1. Serialization is ~1.8× slower than System.Text.Json

`MEASURED`, codec suite, default job, RatioSD ≤ 0.02.

| Items | Time ratio vs STJ source-gen | Allocation ratio |
| --- | --- | --- |
| 1 | 1.86× | 1.58× |
| 100 | 1.84× | 1.23× |
| 10,000 | 1.75× | 1.22× |

Down from 2.23–2.52× after caching member property names (see Fixed, below). The
ratio is still roughly **flat across three orders of magnitude of payload**, so
what remains is still a per-element cost in the write path rather than fixed
setup.

Two causes are now accounted for and fixed: buffer churn (allocations) and
per-member name resolution and encoding (time). The remaining ~1.8× has not been
attributed. Untested leads:

- Per-member and per-value interface dispatch (`IJsonMemberWriter<T>.Write`,
  `IJsonValueWriter<T>.Write`) where source generation emits straight-line calls
- `member.GetValue(container)` indirection per member per object
- The nullable/default-materialization branch evaluated per member per object,
  including a second trait lookup inside `TryCreateDefaultValue` on the null path
- Boxing in the value-writer chain for value-typed members

A profiler run is still the honest next step; the property-name fix was found by
reading the code, but three other predictions made the same way were wrong today.

### 2. The client pays fixed per-call overhead that dominates small calls

`MEASURED` for the shape, `HYPOTHESIS` for the causes.

The NSmithy client's ratio against the hand-written ceiling decays as payload
grows, 2.5× on `get-item`, 1.35× on the 10,000-item response, which is the
signature of a constant per-call cost being amortized rather than a per-byte one.

Costs visible in `SmithyClientRuntime.InvokeCoreAsync`, all unconditional:

- `OBSERVED` **Telemetry allocates even with no listener.** The activity name is
  built with string interpolation and passed to `StartActivity`, which then
  returns null when nothing is subscribed. `binding.ServiceId.ToString()` is
  called per invocation to populate a `TagList` only a metrics exporter reads.
  Both values are constant per binding and could be computed once at construction
  or guarded behind `ActivitySource.HasListeners()`.
- `OBSERVED` `SmithyClientTelemetry.OperationDuration.Record(...)` runs in the
  `finally` on every call.
- `OBSERVED` A context object per invocation, endpoint resolution with its own
  parameter and result allocations even for a static endpoint, and auth-scheme
  selection per call.

Caveat worth keeping: some of this gap is **features, not waste**. Retry,
interceptors, telemetry and endpoint resolution are things the hand-written and
NSwag clients do not do at all. The unambiguous waste is only the part paid when
those features are inactive.

### 3. NSmithy's client is the slowest of the three measured

`MEASURED`. Against both the hand-written ceiling and NSwag's generated client, on
every scenario. NSwag is close to hand-written on small calls despite using
reflection-based `System.Text.Json`, which suggests the gap is not inherent to
being generated.

### 4. List wrapper types defensively copy

`OBSERVED`, cost not isolated.

Generated list shapes wrap their contents:

```csharp
Values = System.Array.AsReadOnly(System.Linq.Enumerable.ToArray(values));
```

That is an array copy plus two allocations per list instance, which the
`System.Text.Json` baselines never pay. It is left in the measurement deliberately
,  it is a real cost of the generated types, but it is a candidate for removal
when the caller already hands over an array it owns.

### 5. Codec optimization candidates, by codec

`OBSERVED` from source. None isolated by measurement, and only JSON is benchmarked
at all, CBOR, XML and proto have no coverage in this suite.

**JSON read path, materializes a DOM.**

```csharp
using var document = JsonDocument.Parse(payload);
return valueReader.Read(document.RootElement);
```

Every reader in the compiled chain takes a `JsonElement`, so the whole payload is
parsed into a document before any value is produced. `System.Text.Json`'s source
generator uses a forward-only `Utf8JsonReader` and never builds one. This is the
most likely explanation for the read path's 1.38–1.44× time and 1.17–1.29×
allocation gap that remains after the member-lookup fix below (now 1.14–1.15×
time, 1.11–1.24× allocations). Worth noting the CBOR codec already reads
forward-only via `CborReader`, so JSON is the outlier here, not the norm.

**CBOR write path, both JSON fixes apply unchanged.**

`CompiledCborCodec.Serialize` allocates a fresh `CborWriter` per call and returns
`writer.Encode()`, a newly allocated array, the same pattern that was fixed in
JSON. `CborWriter` exposes `Reset()` and `TryEncode(Span<byte>, out int)`, so the
pooled-buffer and reused-writer treatment transfers directly.

`CborMemberWriter` calls `writer.WriteTextString(member.Name)` per member per
object, re-encoding a constant name to UTF-8 every time. CBOR has no
`JsonEncodedText` equivalent, but the encoded text-string bytes can be computed
once and emitted with `WriteEncodedValue`, which is the same fix in a different
shape.

**XML codec, worse than JSON ever was, on both sides.**

```csharp
// serialize: DOM, then a string, then bytes
var root = new XElement(RootElementName(schema));
return Encoding.UTF8.GetBytes(root.ToString(SaveOptions.DisableFormatting));

// deserialize: bytes, then a string, then a DOM
var root = XElement.Parse(Encoding.UTF8.GetString(payload));
```

Each direction builds a full LINQ-to-XML document *and* a full intermediate
string, so a payload is materialized three times over. `XmlWriter`/`XmlReader`
over a pooled buffer would avoid the DOM and both string copies. This is
unmeasured, but structurally it is the most expensive codec in the repo.

**Shared: a second trait lookup on the null path.** Both the JSON and CBOR member
writers call `TryCreateDefaultValue(member.TargetSchema, member.MemberTraits, ...)`
for every optional member that is null, which re-enters trait resolution per member
per object. Whether a member has a default is constant per member and resolvable at
compile time, like the property name was.

**No coverage at all for CBOR, XML or proto.** Every number in this document is
restJson1. The fixes already landed in the JSON codec have visible analogues in
CBOR, and nothing verifies whether they would help or whether the other codecs have
different problems entirely. Extending the codec suite to the others is cheap, it
needs no server, no HTTP, and no parity gate.

---

## Fixed

### Quadratic member lookup on the read path

`MEASURED`. `StructureJsonValueReader.Read` called `value.TryGetProperty(name)`
once per member, and each of those calls scanned the object again, so a structure
with N members over an object with M properties walked the document N times.

Replaced with a single pass over the payload, matching each property against
member names pre-encoded as UTF-8 via `JsonProperty.NameEquals`, with seen-member
tracking in a stack-allocated span. No name strings are materialized and nothing
is allocated per read.

| Scenario | Before | After | Gain |
| --- | --- | --- | --- |
| create-order-small | 5,906 ns | 4,651 ns | 21% |
| create-order-large | 5,060 µs | 4,158 µs | 18% |

Deserialization against `System.Text.Json` source-gen went **1.38–1.44× to
1.14–1.15×**, with allocations 1.17–1.29× to 1.11–1.24×. The serialization
benchmarks, which the change does not touch, held at 1.75–1.82×, confirming the
effect is confined to the read path.

On a payload with a duplicate key the first occurrence still wins, matching what
`TryGetProperty` did.

The rewrite surfaced a latent correctness bug, fixed separately, see below.

Verified across 86 parity tests and the full 1,203-test suite.

### Per-member property name resolution and encoding

`MEASURED`. `JsonMemberWriter.Write` resolved each member's wire name on every
write, a `ShapeId`-keyed dictionary lookup for a possible `@jsonName`, then
`WritePropertyName(string)`, which re-transcoded and re-escaped the name to UTF-8
every time. All of it is constant per member.

Now encoded once at compile time into a `JsonEncodedText`, which is what
`System.Text.Json`'s source generator does.

| Items | Before | After | Gain |
| --- | --- | --- | --- |
| 1 | 472 ns | 364 ns | 23% |
| 100 | 37,765 ns | 28,670 ns | 24% |
| 10,000 | 4,411,963 ns | 3,534,822 ns | 20% |

Allocations were unchanged, as expected, this was never an allocation problem.
The deserialization benchmarks, which the change does not touch, moved 1.35×/1.45×
to 1.38×/1.44×, confirming the effect is confined to the write path.

Verified byte-identical output across 86 parity tests and the full 1,203-test
suite (unit plus all five protocol conformance suites).

### Serialization buffer churn

`MEASURED`. `CompiledJsonCodec.Serialize` used a `MemoryStream` (reallocating and
copying on every growth) followed by `ToArray()` (copying the whole payload
again), plus a fresh `Utf8JsonWriter` per call.

Replaced with an `ArrayPool`-backed buffer writer, a thread-static writer reused
via `Reset()`, and a per-instance size hint. Allocations dropped 2.9–3.9×:

| Items | Before | After |
| --- | --- | --- |
| 1 | 1,120 B | 392 B |
| 100 | 85,032 B | 21,904 B |
| 10,000 | 8,044,360 B | 2,205,234 B |

Verified byte-identical output: 48 parity + 189 unit + 480 restJson1 + 28 AwsJson
tests. **Bought no measurable time**, see open problem 1.

---

## Correctness findings

### An explicit null skipped modelled defaults on one read path

`MEASURED` by probe, now fixed and regression-tested.

The two JSON structure readers disagreed about what an explicit `null` means for a
member carrying `@default`:

```
projection reader: absent=7, explicitNull=7      // default applied
value reader:      absent=7, explicitNull=-1     // default skipped, member left unset
```

`-1` is a test sentinel, the value reader left the member untouched, so the
modelled default never materialized. Smithy guarantees a member with `@default`
always has a value, so that produced an object the model says cannot exist; in
generated code the member would land on `default(T)` or null rather than the
modelled default.

The value reader was the incorrect one. It now leaves a null-valued member unseen
so the trailing pass applies the default, matching the projection reader. The
blast radius is narrow: for a member without `@default`, materialization is a
no-op either way.

A conformance harness now covers it:
`tests/Conformance/RestJson1/model/defaulted-member-null.smithy`, with cases for
explicit null, absent, and present. Two details were load-bearing and verified by
deliberately breaking them, the members must be **nested** (a REST operation's
top-level output goes through the projection reader, which was already correct),
and the null/absent cases must be `appliesTo: "client"` (a server given `count: 7`
serializes it, so it could never emit that body).

Writing that harness uncovered something larger, see below.

### Conformance response assertions were a silent no-op

`MEASURED`, fixed for restJson1, **still present in four other protocol suites**.

`AssertStructure` looked up expected values by generated constructor parameter
name, which codegen emits as PascalCase (`Nested`), against fixture `params` keys,
which are the model's camelCase member names (`nested`). Every lookup missed and
took the "omitted fields are not asserted" path. Proof: an expected value of `999`
against an actual of `null` passed.

So the suites asserted status codes, headers, and that deserialization did not
throw, but never that the deserialized *values* were right.

Fixing the lookup turned **52 restJson1 cases red** for the first time. They are
quarantined in `KnownResponseParamGaps` (20) and `KnownServerRequestParamGaps`
(32), clustered in unions, streaming blobs, and query-string binding. They are not
newly broken, just newly reachable; each needs triage, and some may be limitations
in the runner's own union and blob comparison logic rather than NSmithy bugs.

The reported conformance rate moved accordingly, and the gaps are subtracted from
it so the docs Protocol Status page is not overstated:

| Direction | Reported before | Actual |
| --- | --- | --- |
| client-responses | 109/109 (100%) | 89/109 (81.7%) |
| server-requests | 133/136 (97.8%) | 101/136 (74.3%) |

Now fixed in all five suites. Across the repo, **81 cases** had assertions that
could not fail; they are quarantined in `KnownParamGaps` (per suite) and
`RestJson1Allowlist.Known*ParamGaps`.

Corrected rates, after subtracting the quarantined cases from every report:

| Protocol | Direction | Reported before | Actual |
| --- | --- | --- | --- |
| restJson1 | client-responses | 100% | 81.7% |
| restJson1 | server-requests | 97.8% | 74.3% |
| rpcv2Cbor | client-responses | 100% | 78.0% |
| rpcv2Cbor | server-requests | 100% | 91.4% |
| simpleRestJson | client-responses | 100% | 65.0% |
| simpleRestJson | server-requests | 100% | 78.3% |
| restXml | responses | 48.8% | 45.3% |
| awsJson1_1 | client-responses | 31.1% | 27.9% |

Two runner defects accounted for 25 of the original 106, and fixing them was the
right first move, quarantine lists built against a broken runner overstate the
real gaps:

- **Blobs were compared as sequences.** Only the restJson1 and awsJson runners had
  a `byte[]` branch; the other three fell through to the list comparison and threw
  `node must be of type 'JsonArray'` with no path. Ported the branch, which tries
  base64 and falls back to UTF-8 text.
- **`sbyte` was not a known scalar.** Smithy's `Byte` maps to C# `sbyte`, which was
  missing from the numeric comparison, so every signed-byte member failed with
  "don't know how to compare scalar".

`AssertSequence` now also fails with the member path and runtime type instead of
throwing a bare cast exception, which is what made these diagnosable at all.

**There is no CBOR defaults gap.** An earlier note here speculated one, because
rpcv2Cbor's `ClientPopulatesDefaultsValuesWhenMissingInResponse` and
`ServerPopulatesDefaultsWhenMissingInRequestBody` were failing. Both were the blob
and `sbyte` defects above; the CBOR reader materialises defaults for absent members
correctly, and all three defaults cases now pass. Recorded as a reminder that a
failing test names a symptom, not a cause.

The remaining clusters: unions (~40 cases), streaming and payload blobs,
query-string binding, and float special values (NaN/Infinity), the last now
isolated to rpcv2Cbor and restJson1 query binding.

One process note: xunit truncates long case ids in its failure output, so a first
sweep of the failures missed seven of them. Collect quarantine lists from the
model rather than from console output.

### NSmithy's OpenAPI output drops integer types

`OBSERVED`, and **unfixed in the product**.

`SmithyOpenApiProtocol` does not set `useIntegerType` on the `smithy-openapi`
plugin, so every emitted document describes Smithy `Integer` and `Long` members as
`{"type": "number"}` with no format. Downstream generators then produce untyped
numbers, importing into TypeSpec yielded `numeric`, which its C# emitter mapped
to `object`, boxing every number.

Only the benchmark's own `smithy-build.json` was patched. The real fix belongs in
`packages/NSmithy.MSBuild/Tasks/SynthesizeSmithyBuildFile.cs`, which writes the
`openapi` plugin block and currently emits only `service` and `protocol`. This
affects anyone generating clients from NSmithy's OpenAPI output.

### Third-party generator gaps

`OBSERVED`, from generated source.

- **`@typespec/http-server-csharp` 0.58.0-alpha.30** cannot serve this contract:
  response header bindings are emitted into the JSON body as a wrapper property,
  and `statusCode` is serialized as a body property. Nothing in its output writes
  a response header at all. Excluded from the suite rather than patched, see
  `stacks/TypeSpec/README.md`. This is an emitter limitation, not a limitation of
  the TypeSpec language.
- **NSwag** clients discard response headers entirely. The modelled
  `x-total-count` on SearchItems is unreachable through the generated API; the
  suite reaches it via NSwag's `ProcessResponse` extension point.

### The hand-written client baseline is still not a ceiling

`MEASURED`, and only partly resolved.

It used the default `HttpCompletionOption.ResponseContentRead`, which buffers the
entire response before deserializing. Switching to `ResponseHeadersRead` cut
allocations across the board:

| Scenario | Before | After |
| --- | --- | --- |
| get-item | 2.90 KB | 2.66 KB |
| list-items-100 | 71.05 KB | 56.70 KB |
| list-items-10000 | 6,940 KB | 5,491 KB |

But on the 10,000-item response it also got **slower**, 6.56 ms → 7.13 ms, and
NSwag still wins on both axes there (5.67 ms / 3,930 KB). So the baseline
allocates ~1.5 MB more than a generated client while doing the same job, roughly
one extra copy of the response body, and nobody has established why. Both stream
from the response now; the remaining differences are `HttpContent.ReadFromJsonAsync`
versus an explicit `ReadAsStreamAsync` + `JsonSerializer.DeserializeAsync`, and
the DTO collection types the two generators chose.

Until that is closed, **treat `hand-written` as a strong reference point rather
than a true ceiling on the large-response scenarios**, and do not quote "n% of
hand-written" for `list-items-10000`.

Worth remembering as a category: a hand-written baseline is only a ceiling if it
is actually well written, and a generated client beating it is a signal to check
the baseline before believing the generator is fast.

### MVC hosting costs more than generated code does

`MEASURED`. On `create-order-large` the hand-written MVC baseline allocates
13,055 KB, worse than NSmithy's 9,067 KB and 2.7× the minimal-API baseline's
4,768 KB.

This is why the suite carries a hand-written MVC baseline. Both TypeSpec and NSwag
generate MVC controllers, so without it a chunk of MVC hosting overhead would be
misattributed to their generated code.

---

## Environment and tooling

### BenchmarkDotNet's default toolchain hangs under Nix

`OBSERVED`, root cause not pinned down.

Its out-of-process toolchain generates and builds a throwaway project per run. On
this repo's Nix/devenv setup that step resolves an SDK root inside `/nix/store`
and recursively enumerates it, which never finishes. On a hung run the main thread
sits in `Monitor_Wait` while a worker burns samples in `OpenDir`/`ReadDir`, with
135 open handles under `/nix/store`. `--buildTimeout` does not help, the walk
happens before the build, where no timeout applies.

Worked around with `--inProcess` in every `just bench-*` recipe. Which specific
BenchmarkDotNet code path computes that root is still unknown.

### Formatting cannot run in this worktree

`OBSERVED`. `csharpier` finds zero files anywhere under
`.claude/worktrees/performance-suite`, because the whole path is inside gitignored
`.claude/`. The benchmark sources have never been formatted; that needs a run from
the main checkout.

---

## What the suite still cannot answer

- **Where the 1.8× serialization time goes.** Open problem 1, and the top
  priority.
- **How member lookup scales with structure width.** The read path matches each
  payload property against member names by linear scan over pre-encoded UTF-8
  (`JsonProperty.NameEquals`), which is O(properties × members). It is deliberately
  not a dictionary: hashing needs `JsonProperty.Name`, which allocates a string per
  property, whereas the scan allocates nothing. At typical widths it is clearly not
  dominating, deserialization sits at 1.14–1.15× of `System.Text.Json` source-gen
 , but the widest shape in the benchmark model is six members, so the suite cannot
  see a wide-structure cliff. A 64-member shape plus a corpus scenario would settle
  it, and would also show whether the write path is width-sensitive. If it does
  degrade, the fix stays allocation-free: bucket members by name length or dispatch
  on length plus first byte, which is roughly what source generation compiles to.
  Deferred until the conformance work has landed, to avoid perturbing it.
- **How much client overhead is telemetry versus features.** Needs a
  config-stripped variant, deliberately not built, tuning one contender to win is
  what the parity gate exists to prevent. A before/after on the unconditional
  telemetry allocations would answer it without that risk.
- **Throughput and tail latency under concurrency.** No socket-level macro suite
  exists; everything here is in-memory and single-threaded.
- **Anything about protocols other than restJson1.** No CBOR, proto, or XML
  coverage.
- **Anything about TypeSpec's performance.** It has never run in this suite.
