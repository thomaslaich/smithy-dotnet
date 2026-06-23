# Client Guidance Gaps

Where NSmithy's generated client stands against Smithy's
[client implementation guidance](https://smithy.io/2.0/guides/client-guidance/index.html),
and the client-runtime shape we want to grow toward.

## Goal

Track which client-runtime behaviors the guidance recommends, which NSmithy
already provides, and what runtime shape would let the remaining pieces compose.
The current architecture (`SmithyOperationInvoker` + `ISmithyClientMiddleware` +
`IProtocol`) proves the protocol abstraction, but it puts extension points too
late in the request lifecycle. Middleware sees serialized HTTP, not the typed
operation invocation, so features such as endpoint resolution, per-operation
auth, standard retries, and telemetry each have to work around missing context.

The desired direction is a client lifecycle orchestrator: one place that owns the
operation execution flow, creates per-call context, runs named hooks, resolves the
endpoint and auth scheme, serializes, signs, transmits, retries, deserializes, and
completes the operation. Existing middleware is best understood as today's
limited interceptor: it wraps the final HTTP send stage. The next refactor should
preserve that use case while promoting a richer interceptor model as the primary
extension point.

Reference implementations (smithy-java and smithy-kotlin) are useful for
checking terminology and edge cases, but they should not dictate NSmithy's public
shape. The target is an idiomatic .NET runtime with explicit lifetimes,
`HttpClientFactory` integration, `ActivitySource`/`Meter` telemetry, and
`IAsyncEnumerable<T>` for generated paginators.

## Current coverage

| Guidance area | Status | Notes |
| --- | --- | --- |
| HTTP fields: case-insensitive, multi-valued, no comma-joining | ✅ | `SmithyHttpRequest.Headers` |
| Configurable transport (`ClientTransport`) | ✅ | `IHttpTransport`, injectable `HttpClient` |
| Protocol selection | ✅ | `IProtocol` ctor param, defaults to the primary declared protocol |
| Request compression / content-MD5 | ✅ | `SmithyRequestModifiers` (`@requestCompression`, `@httpChecksumRequired`) |
| Auth scheme resolution | 🟡 | Service-level priority resolution and explicit SigV4 signing exist (see [auth](#auth-follow-ups)). Missing per-operation override, identity/signer split, endpoint-driven override |
| Retries | 🟡 | `SmithyRetryMiddleware` retries 429/5xx with a fixed delay only |
| Endpoint resolution | ❌ | Client config carries a static `Endpoint`; no resolver, host-prefix, or per-operation resolution |
| Interceptors / typed request context | 🟡 | `ISmithyClientMiddleware` is a send-stage hook only; no typed operation context or named lifecycle stages |
| Request timeout via context | ❌ | Only `HttpClient.Timeout` |
| Streaming request/response bodies | ❌ | `SmithyHttpRequest.Content` is `byte[]` (fully buffered) — see [Gap 4](#gap-4--streaming-bodies) |
| Pagination (`@paginated`) | ❌ | No generated paginators — see [Gap 5](#gap-5--pagination) |
| Observability / telemetry | ❌ | No tracing/metrics; `ActivitySource`/`Meter` available in the BCL — see [Gap 6](#gap-6--telemetry) |
| Identity caching / refresh | ❌ | `IAwsCredentialsProvider` resolves but has no expiry/caching layer |
| Client configuration / construction | ✅ | Generated `{Service}ClientConfig` (endpoint, protocol, auth, middleware, idempotency); `endpoint` / `httpClient` / `invoker` ctors; `IDisposable` — see [Gap 7](#gap-7--client-configuration--construction) |
| User-Agent | ❌ | Not set |

The gaps below are ordered by dependency: **Gap 1 (client lifecycle + typed
context + interceptors)** is foundational; **endpoint resolution**, **retries**,
**telemetry**, and per-operation auth all build on it. Streaming and pagination
are independent.

## Gap 1 — Client lifecycle + typed context + interceptors (foundational)

The core problem is not that NSmithy has no extension point. It does:
`ISmithyClientMiddleware` can observe and modify the final `SmithyHttpRequest`
around transport send. That is useful, and it already supports simple auth,
header mutation, and simple retry middleware.

The problem is that this hook sits too late. Today the generated client
serializes the typed input before calling `SmithyOperationInvoker`, and
`SmithyOperationRequest` carries only `(ServiceName, OperationName,
SmithyHttpRequest)`. Middleware cannot see the typed input/output, cannot store
typed per-call state, cannot participate in endpoint parameter construction, and
cannot distinguish one overall execution from individual retry attempts.

We want the runtime to own the full client lifecycle:

```text
start execution
  → prepare typed input/context
  → resolve endpoint
  → serialize request
  → resolve auth
  → sign request
  → transmit attempt
  → deserialize response/error
  → complete execution
```

That lifecycle needs named interceptor hooks and a per-call context object. The
context should be open enough for runtime features and user interceptors, while
remaining type-safe:

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
   a client interceptor with named hooks, so cross-cutting code can observe typed
   input/output and the right lifecycle stage. Start with the hooks NSmithy needs
   now rather than trying to clone another runtime's full list:

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

   Existing `ISmithyClientMiddleware` maps naturally to the send-stage hooks
   (`ModifyBeforeTransmit` + `ReadAfterTransmit`). Keep it as a compatibility
   adapter while new features move to interceptors.

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

**Proposed:** replace/upgrade `SmithyRetryMiddleware` into a standard retry
strategy owned by the lifecycle orchestrator:

- Exponential backoff `base * 2^(attempt-1)` with full jitter, capped (~20s).
- Token bucket (throttling errors cost more; successes refund) shared per client.
- Honor `Retry-After` as a minimum delay.
- Classify retryability: `@retryable` (with `throttling`), transient transport
  failures, 429, and 5xx.
- Backoff/jitter timing reads from `TimeProvider` (already injected into
  `AwsSigV4Middleware`) so it is deterministically testable.

The following gaps are not all in the original guidance checklist, but they
matter for a useful generated .NET client.

## Gap 4 — Streaming bodies

`SmithyHttpRequest.Content` is `byte[]`, so request and response bodies are fully
buffered in memory. Streaming matters for `@streaming` members and large payloads
(S3 objects, uploads). The idiomatic .NET answer is `Stream` /
`System.IO.Pipelines` / `IAsyncEnumerable<ReadOnlyMemory<byte>>`.

Retrofitting a streaming body type after the surface area grows is painful, so the
body abstraction is worth deciding early even if streaming codegen lands later.
Note the interaction with SigV4: streaming bodies need `UNSIGNED-PAYLOAD` or
chunked signing rather than the always-on `X-Amz-Content-Sha256` over a buffered
body (see [auth](#auth-follow-ups)).

## Gap 5 — Pagination

`@paginated` operations generate no paginator today; callers loop on
`nextToken` manually. The idiomatic .NET surface is an `IAsyncEnumerable<T>`
consumed with `await foreach`, generated from the trait's `inputToken` /
`outputToken` / `items` bindings. Self-contained codegen + runtime helper; no
dependency on Gap 1.

## Gap 6 — Telemetry

No tracing or metrics today. .NET already has the right primitives:
`ActivitySource` for spans and `Meter` for metrics, both OpenTelemetry-friendly.
Once Gap 1's orchestrator exists, a span per operation/attempt and a few counters
(attempts, retries, latency) are low effort. Builds on Gap 1.

## Gap 7 — Client configuration / construction

**Shipped.** Clients use a per-service
`{Service}ClientConfig : SmithyClientConfig`, a mutable config object with public
setters, populated inline by a C# object initializer. Normal callers pass the
endpoint directly and add config only when they need extra knobs:

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
- **Endpoint lives in config internally**, while the public API keeps an endpoint
  convenience overload for the common case. The endpoint argument is copied into
  config and wins over any existing `Config.Endpoint`. When Gap 2 lands, the
  resolver becomes another config knob and `Config.Endpoint` is the static
  override that wins over it.
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
  the client created it (a no-op when an `HttpClient` or invoker was supplied).

A config object rather than named constructor parameters because it is a nameable,
reusable value that the DI options pattern can take, and — decisively for a
versioned library — adding a property is backward-compatible, whereas adding a
constructor parameter is a binary-breaking change. The endpoint convenience
constructor is the exception because endpoint is the minimum viable client input,
not an advanced knob.

## Auth follow-ups

Service-level priority resolution shipped (`SmithyAuthSchemeResolver`,
`ISmithyAuthScheme.SchemeId`, codegen `ModeledAuthSchemes` via
`ServiceIndex.getEffectiveAuthSchemes`). Explicit SigV4 signing also exists via
`AwsSigV4AuthScheme`, with callers providing the signing service, region, and
credentials provider directly. That is enough for narrow real-AWS smoke tests,
but not yet an AWS SDK-style auth stack. Remaining, in rough priority:

- Per-operation `@auth` override (needs per-operation resolution; pairs with the
  pipeline being built once at construction today).
- Identity/signer separation so one credential set can feed multiple schemes —
  deferred; today credentials are baked into the scheme.
- Identity caching / refresh: `IAwsCredentialsProvider` resolves credentials but
  has no expiry/caching layer. Expiring identities and runtime caching are needed
  the moment STS / SSO / IMDS providers land.
- Credential provider chain: environment variables exist, but profile, SSO,
  process, ECS, EC2 IMDS, and web-identity providers do not.
- Endpoint-driven auth override (see Gap 2, step 3).
- SigV4: pin a golden vector from AWS's `sig-v4-test-suite`, and revisit the
  always-on `X-Amz-Content-Sha256` header (an S3-ism; affects which canonical
  vectors validate) and `UNSIGNED-PAYLOAD` for streaming bodies.
