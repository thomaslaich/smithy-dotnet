# Client Guidance Gaps

Where NSmithy's generated client stands against Smithy's
[client implementation guidance](https://smithy.io/2.0/guides/client-guidance/index.html),
and a proposed design for the structural gaps.

## Goal

Track which client-runtime behaviors the guidance recommends, which NSmithy
already provides, and how we intend to close the remaining gaps. The existing
protocol-agnostic architecture (`SmithyOperationInvoker` + `ISmithyClientMiddleware`
+ `IProtocol`) is the starting point — but closing the foundational gap (Gap 1)
deliberately evolves it from an onion of HTTP middleware toward a lifecycle
orchestrator (see below), so the smaller gaps compose instead of each being a
special case.

## Reference implementations

We benchmark against two mature Smithy client runtimes:

- **smithy-java** — closest to our shape (sync/async, `Identity`/`Signer`).
- **smithy-kotlin** (AWS SDK for Kotlin) — implements the Smithy **interceptor**
  spec on top of a lifecycle orchestrator with a typed `ExecutionContext`. This
  is the model the foundational gap borrows from.

The structural lesson from smithy-kotlin: NSmithy middleware only ever sees an
**already-serialized `SmithyHttpRequest`** (serialization happens in the generated
client *before* `invoker.InvokeAsync`), and `SmithyOperationRequest` carries no
typed input/output and no context bag. A single `InvokeAsync(request, next)` hook
is roughly *one* lifecycle stage (`modifyBeforeTransmit` + `readAfterTransmit`)
wrapped around the send. smithy-kotlin instead orchestrates the whole operation
(serialize → resolve endpoint → resolve auth → sign → transmit → deserialize →
retry) with ~13 named hook points, threading typed context throughout. Endpoints,
standard retry, telemetry, and per-operation auth all fall out of that once it
exists.

## Current coverage

| Guidance area | Status | Notes |
| --- | --- | --- |
| HTTP fields: case-insensitive, multi-valued, no comma-joining | ✅ | `SmithyHttpRequest.Headers` |
| Configurable transport (`ClientTransport`) | ✅ | `IHttpTransport`, injectable `HttpClient` |
| Protocol selection | ✅ | `IProtocol` ctor param, defaults to the primary declared protocol |
| Request compression / content-MD5 | ✅ | `SmithyRequestModifiers` (`@requestCompression`, `@httpChecksumRequired`) |
| Auth scheme resolution | 🟡 | Service-level priority resolution (see [auth](#auth-follow-ups)). Missing per-operation override, identity/signer split, endpoint-driven override |
| Retries | 🟡 | `SmithyRetryMiddleware` retries 429/5xx with a fixed delay only |
| Endpoint resolution | ❌ | Client config carries a static `Endpoint`; no resolver, host-prefix, or per-operation resolution |
| Interceptors / typed request context | ❌ | Pipeline threads only `(service, operation, httpRequest)`; middleware sees serialized HTTP only |
| Request timeout via context | ❌ | Only `HttpClient.Timeout` |
| Streaming request/response bodies | ❌ | `SmithyHttpRequest.Content` is `byte[]` (fully buffered) — see [Gap 4](#gap-4--streaming-bodies) |
| Pagination (`@paginated`) | ❌ | No generated paginators — see [Gap 5](#gap-5--pagination) |
| Observability / telemetry | ❌ | No tracing/metrics; `ActivitySource`/`Meter` available in the BCL — see [Gap 6](#gap-6--telemetry) |
| Identity caching / refresh | ❌ | `IAwsCredentialsProvider` resolves but has no expiry/caching layer |
| Client configuration / construction | ✅ | Generated `{Service}ClientConfig` (endpoint, protocol, auth, middleware, idempotency); `endpoint` / `httpClient` / `invoker` ctors; `IDisposable` — see [Gap 7](#gap-7--client-configuration--construction) |
| User-Agent | ❌ | Not set |

The gaps below are ordered by dependency: **Gap 1 (interceptor lifecycle +
context)** is foundational; **endpoint resolution**, **retries**, **telemetry**,
and per-operation auth all build on it. Streaming and pagination are independent.

## Gap 1 — Interceptor lifecycle + typed context (foundational)

The guidance recommends each operation invocation create its own open, type-safe
context map (a generic `Key<T>` that encodes the value type), passed through the
pipeline so plugins, retries, timeouts, endpoint params and auth can read and
write per-call state. smithy-kotlin realizes this as an **`ExecutionContext`**
threaded through a lifecycle orchestrator, with **interceptors** hooking named
stages.

Today `SmithyOperationRequest` carries only `(ServiceName, OperationName,
Request)`, and the generated client serializes the input *before* calling the
invoker — so middleware can neither read the typed input/output nor stash typed
per-call state, and there is no per-attempt vs per-execution distinction.

**Proposed (two parts):**

1. A `SmithyContext` of `IReadOnlyDictionary`-backed typed keys, one instance per
   invocation:

   ```csharp
   public sealed class ContextKey<T>(string name);

   public sealed class SmithyContext
   {
       public T? Get<T>(ContextKey<T> key);
       public void Set<T>(ContextKey<T> key, T value);
   }
   ```

2. Move serialization/deserialization *into* the orchestrated pipeline and expose
   a `ClientInterceptor` with named hooks, so cross-cutting code can observe the
   typed input/output and the right lifecycle stage (mirroring smithy-kotlin's
   ~13 hooks; start with the ones we need):

   ```csharp
   public interface IClientInterceptor
   {
       // per-execution (run once, outside the retry loop)
       void ReadBeforeExecution(SmithyContext ctx);
       object? ModifyBeforeSerialization(SmithyContext ctx, object? input);
       SmithyOperationResponse ModifyBeforeCompletion(SmithyContext ctx, SmithyOperationResponse r);

       // per-attempt (run each retry attempt)
       SmithyHttpRequest ModifyBeforeSigning(SmithyContext ctx, SmithyHttpRequest req);
       SmithyHttpRequest ModifyBeforeTransmit(SmithyContext ctx, SmithyHttpRequest req);
       void ReadAfterTransmit(SmithyContext ctx, SmithyHttpResponse resp);
   }
   ```

   Existing `ISmithyClientMiddleware` is the degenerate case of
   `ModifyBeforeTransmit` + `ReadAfterTransmit` wrapped around the send; it can be
   kept as a thin adapter while interceptors become the primary extension point.

This is the largest change but it is what makes Gaps 2/3/6 and per-operation auth
compose rather than each threading state ad hoc. It also unblocks request timeouts
via context (`HTTP_REQUEST_TIMEOUT`).

## Gap 2 — Endpoint resolution

The guidance recommends a per-operation `EndpointResolver` returning an
`Endpoint` (URI + context + optional auth-scheme overrides), with a static URI
taking precedence over a configured resolver, plus `@hostLabel` host-prefix
support and (eventually) rules-engine rulesets.

Today the endpoint is a static `Uri` on client config, usually supplied through
the endpoint convenience constructor; `@endpoint` /`@hostLabel` host prefixes and
per-operation endpoints are unsupported.

**Proposed (incremental):**

1. `IEndpointResolver` returning an `Endpoint` record, resolved per operation
   with operation id + input available; default impl wraps the static URI
   (`Endpoint.StaticUri`). Static URI configured ⇒ wins over a resolver.
2. `@hostLabel` host-prefix expansion at request-build time (codegen already
   has the input shape).
3. Endpoint → auth-scheme override (a resolved endpoint may narrow the
   modeled auth schemes — composes with `SmithyAuthSchemeResolver`).
4. Rules-engine ruleset interpretation — later; large, AWS-shaped.

## Gap 3 — Retry strategy

The guidance recommends "standard" mode: exponential backoff with full jitter,
a max-backoff cap, a token-bucket retry quota, honoring `Retry-After`, and
classifying retryability from the `@retryable` trait (throttling vs transient).

`SmithyRetryMiddleware` today retries 429/5xx with a fixed (default zero) delay
and a max-attempt count — no backoff, jitter, quota, or `Retry-After`.

**Proposed:** upgrade `SmithyRetryMiddleware` to standard mode (smithy-kotlin's
`StandardRetryStrategy` is the reference):

- Exponential backoff `base * 2^(attempt-1)` with full jitter, capped (~20s).
- Token bucket (throttling errors cost more; successes refund) shared per client.
- Honor `Retry-After` as a minimum delay.
- Classify retryability: `@retryable` (with `throttling`), transient transport
  failures, 429, and 5xx (smithy-kotlin's `RetryErrorType`:
  Throttling / Transient / ServerError / ClientError).
- Backoff/jitter timing reads from `TimeProvider` (already injected into
  `AwsSigV4Middleware`) so it is deterministically testable.

The following gaps were surfaced by the smithy-kotlin comparison and are not in
the original guidance checklist.

## Gap 4 — Streaming bodies

`SmithyHttpRequest.Content` is `byte[]`, so request and response bodies are fully
buffered in memory. smithy-kotlin streams via `ByteStream` / Flow, which matters
for `@streaming` members and large payloads (S3 objects, uploads). The idiomatic
.NET answer is `Stream` / `System.IO.Pipelines` / `IAsyncEnumerable<ReadOnlyMemory<byte>>`.

Retrofitting a streaming body type after the surface area grows is painful, so the
body abstraction is worth deciding early even if streaming codegen lands later.
Note the interaction with SigV4: streaming bodies need `UNSIGNED-PAYLOAD` or
chunked signing rather than the always-on `X-Amz-Content-Sha256` over a buffered
body (see [auth](#auth-follow-ups)).

## Gap 5 — Pagination

`@paginated` operations generate no paginator today; callers loop on
`nextToken` manually. smithy-kotlin generates `Flow`-based paginators; the
idiomatic .NET equivalent is an `IAsyncEnumerable<T>` consumed with
`await foreach`, generated from the trait's `inputToken` / `outputToken` /
`items` bindings. Self-contained codegen + runtime helper; no dependency on Gap 1.

## Gap 6 — Telemetry

No tracing or metrics today. smithy-kotlin invents a telemetry-provider
abstraction; .NET ships the primitives in the BCL (`ActivitySource` for spans,
`Meter` for metrics), OpenTelemetry-native. Once Gap 1's orchestrator exists, a
span per operation/attempt and a few counters (attempts, retries, latency) are
low effort. Builds on Gap 1.

## Gap 7 — Client configuration / construction

**Shipped.** Clients use a per-service
`{Service}ClientConfig : SmithyClientConfig` — the .NET analogue of smithy-kotlin's
config builder (a mutable config object with public setters, populated inline by a
C# object initializer). Normal callers pass the endpoint directly and add config
only when they need extra knobs:

```csharp
using var client = new DynamoDB20120810Client(
    new Uri("http://localhost:4566"),
    new()
    {
        AuthSchemes = { new AwsSigV4AuthScheme("dynamodb", "us-east-1", creds) },
    });
```

- The config holds `Endpoint`, `Protocol`, `AuthSchemes`, `Middleware`, and
  `IdempotencyTokenProvider`, and is the single home for the knobs Gaps 1–3/6 add
  (interceptors, endpoint resolver, retry strategy, telemetry).
- **Endpoint lives in config internally** (like smithy-kotlin's
  `endpointProvider`), while the public API keeps an endpoint convenience
  overload for the common case. The endpoint argument is copied into config and
  wins over any existing `Config.Endpoint`. When Gap 2 lands, the resolver becomes
  another config knob and `Config.Endpoint` is the static override that wins over
  it.
- Three public constructors select transport ownership: `(endpoint, config?)`
  (normal direct construction; client owns its `HttpClient`), `(httpClient,
  config?)` (endpoint = `Config.Endpoint ?? BaseAddress`; the `AddHttpClient<I,T>`
  / `IHttpClientFactory` path), and `(invoker, config?)` (custom pipeline /
  testing). The endpoint constructor delegates to a private config constructor so
  config remains the internal model without adding another public construction
  style.
- The `HttpClient` constructor is intentionally retained because .NET typed-client
  DI and tests with custom `HttpMessageHandler`s need to pass in an externally
  owned client. The invoker constructor is intentionally retained as the custom
  transport/pipeline escape hatch; generated-client middleware/auth config does
  not apply there because the invoker already owns that pipeline.
- The generated `Add{Service}Client` DI extension takes an
  `Action<{Service}ClientConfig>` callback, so auth / protocol / middleware are all
  configurable through DI.
- Clients implement `IDisposable`: disposing releases the `HttpClient` only when
  the client created it (a no-op when an `HttpClient` or invoker was supplied — the
  analogue of Kotlin's `Closeable`).

A config object rather than named constructor parameters because it is a nameable,
reusable value that the DI options pattern can take, and — decisively for a
versioned library — adding a property is backward-compatible, whereas adding a
constructor parameter is a binary-breaking change. The endpoint convenience
constructor is the exception because endpoint is the minimum viable client input,
not an advanced knob.

## Auth follow-ups

Service-level priority resolution shipped (`SmithyAuthSchemeResolver`,
`ISmithyAuthScheme.SchemeId`, codegen `ModeledAuthSchemes` via
`ServiceIndex.getEffectiveAuthSchemes`). Remaining, in rough priority:

- Per-operation `@auth` override (needs per-operation resolution; pairs with the
  pipeline being built once at construction today).
- Identity/signer separation (smithy-java's `Identity` / `IdentityResolver` /
  `Signer`, smithy-kotlin's `IdentityProvider` + stateless `Signer`) so one
  credential set can feed multiple schemes — deferred; today credentials are
  baked into the scheme.
- Identity caching / refresh: `IAwsCredentialsProvider` resolves credentials but
  has no expiry/caching layer. smithy-kotlin's `IdentityProvider` returns an
  expiring identity the runtime caches and refreshes — needed the moment STS /
  SSO / IMDS providers land.
- Endpoint-driven auth override (see Gap 2, step 3).
- SigV4: pin a golden vector from AWS's `sig-v4-test-suite`, and revisit the
  always-on `X-Amz-Content-Sha256` header (an S3-ism; affects which canonical
  vectors validate) and `UNSIGNED-PAYLOAD` for streaming bodies.
