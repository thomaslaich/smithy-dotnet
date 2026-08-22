# Findings

What the benchmark suite has surfaced, and what is still unexplained.

## How to read this

Every item is tagged with how strongly it is established. The distinction
matters: during this work, reading code produced two confident predictions that
measurement then contradicted.

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

### 1. Full JSON serialization is 1.17–1.35× slower than System.Text.Json with PGO

`MEASURED`, codec suite. Generated structures now supply statically typed values
through `IStructValueSerializer<T>` while `JsonCodec.FromSchema(schema)` remains
the public entry point. This removes `member.GetValue(container)` and the
per-structure member-writer loop from generated shapes without generating any
JSON-specific code.

| Items | PGO disabled | PGO enabled | Execution only, PGO enabled |
| --- | --- | --- | --- |
| 1 | 1.42× | 1.35× | 1.23× |
| 100 | 1.37× | 1.26× | 1.15× |
| 10,000 | 1.29× | **1.17×** | **1.13×** |

The execution-only column reuses the writer and destination buffer. The gap
between it and the full-codec column is therefore setup and output handling, not
member dispatch. A hand-written writer using the same reusable benchmark harness
lands at 1.03×/0.99×/0.99×, establishing that the remaining execution gap is in
the schema-driven writer stack rather than `Utf8JsonWriter` itself.

The original 1.1× target is not reached end to end, but the large payload is
close at 1.17× and allocations remain at parity for the 100- and 10,000-item
payloads. Further improvement should be profiler-led. Likely remaining costs are
per-value codec dispatch and list-schema enumeration; removing those may require
more specialized generated code, while startup expression compilation would be a
poor NativeAOT trade.

### 2. The client pays fixed per-call overhead that dominates small calls

`MEASURED`. A focused invocation benchmark now isolates client ceremony from
serialization and response parsing.

| Path | PGO disabled | PGO enabled | Allocated |
| --- | --- | --- | --- |
| NSmithy | 1.791 µs | 1.388 µs | 4.90 KB |
| Hand-written ceiling | 1.027 µs | 0.838 µs | 2.66 KB |
| Ratio | 1.74× | **1.66×** | 1.84× |

The default path now avoids a `SmithyContext`, endpoint parameters, protocol
delegate bindings, request cloning, interceptor state machines, eager content
header dictionaries, empty response dictionaries, and an unused trailer closure.
Generated output-returning methods also return the runtime task directly.

The NSmithy client's ratio against the hand-written ceiling decays as payload
grows, 2.5× on `get-item`, 1.35× on the 10,000-item response, which is the
signature of a constant per-call cost being amortized rather than a per-byte one.

Against NSwag the same decay is the whole story, and it crosses over:

| Scenario | Time vs NSwag | Allocations vs NSwag |
| --- | --- | --- |
| `get-item` | 2.14× | 2.01× |
| `list-items-1` | 1.78× | 1.74× |
| `search-items` | 1.51× | 2.34× |
| `list-items-100` | 1.35× | 2.07× |
| `list-items-10000` | 1.17× | 2.10× |
| `create-order-small` | 1.16× | 1.23× |
| `create-order-large` | **0.88×** | **0.98×** |

**On the largest request payload NSmithy is faster than NSwag and allocates less.**
A codec problem would get worse with size, not better; this is a constant being
amortized. Quote the whole curve rather than its worst point — and note that only
the small-call end of it is reliable run to run, see below.

**The two columns are two different problems.** Time decays 2.14× → 0.88×, which
is a fixed per-call cost. Allocations do not: they sit near 2× across three orders
of magnitude, which is a *proportional* cost that no per-call feature can explain.
At 10,000 items NSmithy allocates 8.2 MB against NSwag's 3.9 MB, and no amount of
retry policy accounts for 4.3 MB. Two named suspects for that half are already in
this document: the generated list wrappers copying via
`Array.AsReadOnly(Enumerable.ToArray(values))`, and the JSON reader materialising
a `JsonDocument` before producing any value. Do not answer the allocation column
with "we do more" — it is a read-path problem, and it is unsolved.

The remaining focused ratio includes the public runtime, protocol and transport
abstractions plus HTTP request/response objects. Retry, interceptors, telemetry,
dynamic endpoint resolution and auth intentionally select the full path. The
hand-written benchmark implements none of those features, so its number is a
ceiling, not a feature-equivalent comparison.

### The large client scenarios are not comparable across runs

`MEASURED`, the hard way. `create-order-large` and `list-items-10000` allocate
megabytes and collect into Gen2, and their run-to-run spread swamps any change
worth measuring:

- `create-order-large`, **hand-written**, two consecutive runs with no code change
  touching it: 1,543,550 ns → 1,869,799 ns, **+21.1%**.
- `list-items-10000`, **nsmithy**, three runs spanning a change that cannot affect
  the read path: 7,586,168 → 6,879,857 → 7,598,735 ns, a ±5% band with the middle
  run low.

Ratios *within* one run are sound, because every client runs in the same process
under the same conditions — that is what the tables above report. Deltas *between*
runs on these two scenarios are not evidence of anything. A change was nearly
misread as a 10% regression on this basis; the control clients are what settled
it. **Always check the untouched clients moved before believing a delta.**

The small-call scenarios do not have this problem: across the same runs
`hand-written` and `nswag` on `get-item` moved 0.7% and 0.1%.

### 3. NSmithy's client is the slowest of the three on small calls

`MEASURED`. **No longer true on every scenario** — it was when this was written.
NSmithy now beats NSwag on `create-order-large` on both time (0.89×) and
allocations (0.98×), after the codec write-path fixes. The gap that remains is
concentrated in small calls and is open problem 2, not a codec problem.

NSwag is close to hand-written on small calls despite using reflection-based
`System.Text.Json`, which suggests the gap is not inherent to being generated.

### 4. List wrapper types defensively copy

`OBSERVED`, cost not isolated.

Generated list shapes wrap their contents:

```csharp
Values = System.Array.AsReadOnly(System.Linq.Enumerable.ToArray(values));
```

That is an array copy plus two allocations per list instance, which the
`System.Text.Json` baselines never pay. It is left in the measurement deliberately,
since it is a real cost of the generated types, but it is a candidate for removal
when the caller already hands over an array it owns.

### 5. Codec optimization candidates, by codec

`OBSERVED` from source unless a measurement is listed. The microbenchmark suite
now covers JSON, CBOR, XML and proto serialization.

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
over a pooled buffer would avoid the DOM and both string copies. The direct-member
benchmark below was neutral because this architecture dominates the write.

**Shared: a second trait lookup on the null path.** Both the JSON and CBOR member
writers call `TryCreateDefaultValue(member.TargetSchema, member.MemberTraits, ...)`
for every optional member that is null, which re-enters trait resolution per member
per object. Whether a member has a default is constant per member and resolvable at
compile time, like the property name was.

The generated direct-member path measured as follows: CBOR improved about 4% for
one item and 3% for 100 items with unchanged allocations; proto improved from
86.78 ns/248 B to 65.66 ns/208 B, about 24% faster and 16% less allocation; XML
was neutral at 1.90 µs for one item and about 105 µs for 100 items.

---

## Fixed

### Generated structures write typed values directly across codecs

`MEASURED` for JSON, CBOR, XML and proto. Codegen now attaches an
`IStructValueSerializer<T>` to each generated structure schema. It emits only a
format-neutral sequence of indexed, statically typed member values; JSON, CBOR,
XML and proto retain ownership of names, traits, defaults and wire encoding.

This preserves the schema-driven API and one protocol-neutral generated path:
`JsonCodec.FromSchema(personSchema)` still compiles the codec. Hand-built and
projected schemas retain the visitor-based fallback, which keeps existing public
schema construction and projection behavior intact. The full 1,642-test suite
and all 86 benchmark parity cases pass.

### Client telemetry allocated on every call with nothing subscribed

`MEASURED`, client suite.

Two strings were built per invocation and usually thrown away: the span name via
`$"{ServiceId.Name}.{OperationId.Name}"`, handed to `StartActivity`, which returns
null when no listener is subscribed but evaluates its argument regardless; and
`ServiceId.ToString()` for a `rpc.service` tag only a metrics exporter reads. Both
are constant per binding and are now materialized once in
`SmithyOperationBinding`'s constructor as `ActivityName` and `ServiceIdTag`
(internal, so no public surface change).

`OperationDuration.Record`, `Attempts.Add` and `Errors.Add` are now behind
`Instrument.Enabled`, keeping the elapsed-time computation and tag copies off the
unsubscribed path. Separately, `SmithyClientRuntime.interceptors` was
`IReadOnlyList<IClientInterceptor>` and `foreach`'d four times per invocation —
the same boxed-enumerator pattern fixed in the codecs — and is now an array.

| Scenario | Time | Allocations |
| --- | --- | --- |
| `get-item` | **−7.3%** | −2.1% |
| `list-items-1` | **−5.3%** | −2.4% |
| `create-order-small` | −3.0% | −1.2% |
| `search-items` | −0.5% | −0.9% |
| `list-items-100` | +0.3% | −0.2% |
| large scenarios | within run-to-run noise | 0.0% |

The gain lands exactly where the fixed per-call cost lives and disappears once
payload amortizes it, which is the same signature as the problem itself. Against
NSwag, `get-item` went 2.33× to 2.16×.

Control check, because the large scenarios looked like regressions: on `get-item`
the untouched clients moved 0.7% (`hand-written`) and 0.1% (`nswag`) across the
same two runs, so the −7.3% is the change and not the machine.

### Endpoint parameters and context sizing on every call

`MEASURED`, client suite. **An allocation win only — time did not move.**

| Scenario | Allocations | Delta |
| --- | --- | --- |
| `get-item` | 6.94 → 6.73 KB | **−3.03%** |
| `list-items-1` | 6.80 → 6.59 KB | **−3.09%** |
| `create-order-small` | 12.83 → 12.62 KB | −1.64% |
| `search-items` | 18.60 → 18.39 KB | −1.13% |
| `list-items-100` | 87.06 → 86.85 KB | −0.24% |

**A flat ~215 bytes per call**, constant in absolute terms and varying only as a
percentage of payload — the signature of the fixed per-call cost it was meant to
remove. Times moved less than the controls did, so no time claim is made.

This measurement is unusually clean: `hand-written` and `nswag` allocations moved
**0.00%** on every scenario, as they must, since nothing touched them. When the
control is exactly flat, a 215-byte delta on the third client is not noise.

Against NSwag, `get-item` allocations went 2.07× to 2.01×.

Another entry for the lesson at the top of this document: this is the third
allocation fix here to buy no measurable time, against one that did. The one that
did — the boxed enumerator — was an allocation *behind a virtual call in an inner
loop*. These were one record and one dictionary resize per call.

`IEndpointResolver` gained a defaulted `StaticEndpoint` property, so a resolver
that returns the same endpoint whatever it is handed can say so.
`StaticEndpointResolver` answers it, and the runtime now skips building a
`SmithyEndpointParameters` it would only discard — which also keeps the `await`
and its state machine off the path. Defaulted rather than required, so existing
resolvers compile unchanged; a type check against `StaticEndpointResolver` would
have worked too, but this lets any genuinely static resolver opt in.

`SmithyContext`'s dictionary was unsized, so it allocated buckets at three entries
and then reallocated and rehashed when the fourth (`Attempt`) arrived, on every
invocation. It now takes a capacity and the runtime sizes it for the four keys it
sets.

It came in smaller than the telemetry fix, as predicted: that one removed two
string allocations and four boxed enumerators, this removes one record and one
dictionary resize.

One redundant write was left in place deliberately: `CreateContext` sets the
configured endpoint and resolution immediately overwrites it. It is provably dead
— a non-null `endpoint` is always absolute, which always yields a resolver, which
always resolves — but it costs nothing now the dictionary is pre-sized, and
removing it would be a behaviour change for no measurable gain.

### Error responses compiled their writers per response

`MEASURED` for restJson1, `OBSERVED` for rpcv2Cbor. The largest single gap this
suite has found, and it was invisible until a benchmark isolated it.

Both protocols redid per error response what the success path resolves once.

**rpcv2Cbor** built a fresh `CborWriterCompiler` inside `SerializeErrorBody` and
recompiled the error shape's entire writer tree, recursively, per response —
discarding along with it the `SchemaCompilationCache` that exists to stop a shape
being compiled twice.

**restJson1** reached `SerializeStructuredError` through a `(dynamic)` dispatch
per response, then called `Schemas.GetMembers` (a member walk plus a list
allocation), re-derived the header/body member split, rebuilt the projected body
schema, and **recompiled a whole body codec** — every time.

Both now compile in `CompileServerError`, where the operation's other wire work is
compiled, and the compiled result is captured in the closure the error matcher
holds. The shape id and status code stay parameters, because
`MalformedRequestSchema` is one shape serving several of each; that same
compiled writer is now built eagerly in `RestOperationProtocol`, which puts the
validation-failure path on the fast route too.

| | Before | After |
| --- | --- | --- |
| modelled error response | 1,218.3 ns | 309.1 ns |
| vs success response on the same operation | 4.13× | **1.05×** |
| allocated | 3,968 B | 1,256 B |
| allocation ratio | 3.91× | 1.24× |

**3.9× faster, 3.2× fewer allocations**, and an error response now costs
essentially what a success response costs. The residual 1.05×/1.24× is the error
discriminator header and the shape-id string, which are real work.

`RestProtocol.SerializeError` and `RpcV2CborProtocol.SerializeError` stay public
and still compile per call, so ad-hoc callers are unaffected; both now document
that repeat callers should hold the compiled form instead.

A schema-keyed global cache was considered and rejected: `MalformedRequestSchema`
is shared by restJson1, simpleRestJson and restXml, so keying on the schema alone
would have served one protocol's body codec to another. Compiling at the
per-operation seam has the protocol already in hand and cannot alias.

rpcv2Cbor's fix is `OBSERVED` only — it is verified correct by 123 conformance
tests but cannot be measured, because every benchmark scenario in this suite is
restJson1. Since it was recompiling a whole writer tree rather than re-walking a
member list, it was likely worse than 4×.

### Per-member default resolution, hoisted — and unmeasurable here

`OBSERVED`, and **the suite cannot see it**. Recorded as a negative result.

Every write-path member writer called
`TryCreateDefaultValue(member.TargetSchema, member.MemberTraits, out _)` for each
optional member that was null, re-entering trait resolution — two dictionary
lookups — per member per object. Whether a member has a default, and what it is,
are constant per member, exactly like the wire name was, so both are now resolved
once at compile time via `ResolveDefault` (JSON, CBOR, and all four XML member
writers).

It bought nothing measurable: 1.48×/1.48×/1.43× before, 1.49×/1.48×/1.43× after,
allocations identical. The reason is that **the benchmark model never exercises
the path**. `ItemSummary` carries four `@required` members, `BenchDomain` always
populates `category` and `tags`, and `bench.smithy` contains no `@default` at all,
so the null-optional branch is never taken. The only thing removed from the
measured path was the `member.IsRequired` interface call per member, which is in
the noise.

The change is kept: it is strictly less work per null optional member, it is
small, and it is verified correct by the parity gate and full suite. But it is
`OBSERVED`, not `MEASURED`, and it should not be cited as a win.

This is also a **gap in the suite, not only in this change**. `@default`
materialization is idiomatic in Smithy 2.0 models and is exercised by every
conformance suite, but no benchmark scenario reaches it in any codec. A corpus
scenario over a shape with several null optional members carrying `@default`
would measure this, and would also be the first benchmark coverage the default
path has ever had.

Sharing one resolved default instance is safe on the write path, which only
serializes it. It is deliberately **not** hoisted on the read path: `ReadMissing`
sets the default into a builder, where a shared mutable default — a blob, list,
map or document — would alias across deserialized objects.

### A boxed enumerator per structure written

`MEASURED`. The write-path compilers stored their compiled members as
`IReadOnlyList<T>` and `foreach`ed over the *interface*, which binds to
`IEnumerable<T>.GetEnumerator()` and boxes `List<T>.Enumerator` — one heap
allocation, plus non-inlinable interface dispatch on every `MoveNext`/`Current`,
**per structure written**.

Found by arithmetic rather than by profiler. A boxed `List<T>.Enumerator` on
64-bit is exactly 40 bytes (16 header + 8 list reference + 4 index + 4 version +
8 current), and the allocation gap against `System.Text.Json` divided out to 41.0
B/item at 100 items and 40.0 B/item at 10,000 — which is the whole gap, at both
magnitudes.

Changed to `IJsonMemberWriter<T>[]` (and the equivalent for union and open-union
case writers) so `foreach` binds to the array pattern: no enumerator object, and
an inlinable loop body. The reader side was already indexing rather than
enumerating, so only `StructureJsonProjectionReader` changed there; it was
converted for consistency.

| Items | Time before | Time after | Alloc before | Alloc after |
| --- | --- | --- | --- | --- |
| 1 | 1.81× | 1.48× | 392 B | 312 B |
| 100 | 1.83× | 1.48× | 21,904 B | 17,864 B |
| 10,000 | 1.77× | 1.43× | 2,204,836 B | 1,804,689 B |

Allocations landed at **1.00× of `System.Text.Json` source-gen** at 100 and
10,000 items — 17,864 B against 17,800 B, and 1,804,689 B against 1,804,466 B.
The predicted ~400 KB at 10,000 items is gone, to within 223 bytes.

Unlike the pooled-buffer fix, this one **did** buy time, ~19%, because the
enumerator was interface dispatch on the hot loop and not only an allocation.
Worth recording as the counter-example to the lesson at the top of this document:
allocation fixes buy time when the allocation sits behind a virtual call in the
inner loop, and do not when it is a one-per-payload buffer.

Deserialization moved 1.14×/1.18× to 1.12×/1.17×, within noise, confirming the
effect is confined to the write path.

Verified byte-identical output across 86 parity tests and the full suite: 234 unit
plus 1,125 restJson1, 123 rpcv2Cbor, 87 simpleRestJson, 46 restXml and 27 awsJson
conformance tests.

The same change was applied to the CBOR and XML codecs, where the pattern was
identical (`StructureCborValueWriter`, `UnionCborValueWriter`,
`StructureXmlValueWriter`, `UnionXmlValueWriter`, plus their reader counterparts
and the CBOR projection reader). **Those two are `OBSERVED`, not `MEASURED`** —
neither codec has benchmark coverage, so the allocation reduction there is
inferred from the JSON result and the identical shape of the code, not measured.
The 123 rpcv2Cbor and 46 restXml conformance tests verify only that they still
produce correct output. Extending the codec suite to CBOR and XML would settle it.

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

- **Where the 1.43–1.48× serialization time goes.** Open problem 1, and the top
  priority. A throwaway runtime-emit prototype for JSON would establish the
  ceiling before any architectural commitment.
- **Anything about modelled defaults.** `bench.smithy` contains no `@default`, and
  `BenchDomain` populates every optional member, so no scenario in any suite ever
  takes the default-materialization branch that all five codecs carry. This is
  covered by conformance but invisible to the benchmarks; it made the hoisting fix
  unmeasurable.
- **How member lookup scales with structure width.** The read path matches each
  payload property against member names by linear scan over pre-encoded UTF-8
  (`JsonProperty.NameEquals`), which is O(properties × members). It is deliberately
  not a dictionary: hashing needs `JsonProperty.Name`, which allocates a string per
  property, whereas the scan allocates nothing. At typical widths it is clearly not
  dominating: deserialization sits at 1.14–1.15× of `System.Text.Json` source-gen.
  But the widest shape in the benchmark model is six members, so the suite cannot
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
  coverage. This has already cost a real answer once: the error-compilation fix
  (see Fixed) was measured at 3.9× on restJson1 and shipped unmeasured on
  rpcv2Cbor, where the same code was almost certainly worse.
- **Anything about TypeSpec's performance.** It has never run in this suite.
