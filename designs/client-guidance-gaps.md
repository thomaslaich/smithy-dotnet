# Client Guidance Gaps

Where NSmithy's generated client stands against Smithy's
[client implementation guidance](https://smithy.io/2.0/guides/client-guidance/index.html),
and a proposed design for the structural gaps.

## Goal

Track which client-runtime behaviors the guidance recommends, which NSmithy
already provides, and how we intend to close the remaining gaps without
disturbing the existing protocol-agnostic client architecture
(`SmithyOperationInvoker` + `ISmithyClientMiddleware` + `IProtocol`).

## Current coverage

| Guidance area | Status | Notes |
| --- | --- | --- |
| HTTP fields: case-insensitive, multi-valued, no comma-joining | ✅ | `SmithyHttpRequest.Headers` |
| Configurable transport (`ClientTransport`) | ✅ | `IHttpTransport`, injectable `HttpClient` |
| Protocol selection | ✅ | `IProtocol` ctor param, defaults to the primary declared protocol |
| Request compression / content-MD5 | ✅ | `SmithyRequestModifiers` (`@requestCompression`, `@httpChecksumRequired`) |
| Auth scheme resolution | 🟡 | Service-level priority resolution (see [auth](#auth-follow-ups)). Missing per-operation override, identity/signer split, endpoint-driven override |
| Retries | 🟡 | `SmithyRetryMiddleware` retries 429/5xx with a fixed delay only |
| Endpoint resolution | ❌ | Client takes a fixed `Uri`; no resolver, host-prefix, or per-operation resolution |
| Typed request context | ❌ | Pipeline threads only `(service, operation, httpRequest)` |
| Request timeout via context | ❌ | Only `HttpClient.Timeout` |
| User-Agent | ❌ | Not set |

The three structural gaps below are ordered by dependency: **context** is
foundational, **endpoint resolution** and **retries** both build on it.

## Gap 1 — Typed request context (foundational)

The guidance recommends each operation invocation create its own open,
type-safe context map (a generic `Key<T>` that encodes the value type), passed
through the pipeline so plugins, retries, timeouts, endpoint params and auth can
read and write per-call state.

Today `SmithyOperationRequest` carries only `(ServiceName, OperationName,
Request)`. Middleware cannot stash or read typed per-call state.

**Proposed:** a `SmithyContext` of `IReadOnlyDictionary`-backed typed keys:

```csharp
public sealed class ContextKey<T>(string name);

public sealed class SmithyContext
{
    public T? Get<T>(ContextKey<T> key);
    public void Set<T>(ContextKey<T> key, T value);
}
```

Thread it on `SmithyOperationRequest` (one instance per invocation). This is a
small change but unblocks the next two gaps and request timeouts
(`HttpContext.HTTP_REQUEST_TIMEOUT`).

## Gap 2 — Endpoint resolution

The guidance recommends a per-operation `EndpointResolver` returning an
`Endpoint` (URI + context + optional auth-scheme overrides), with a static URI
taking precedence over a configured resolver, plus `@hostLabel` host-prefix
support and (eventually) rules-engine rulesets.

Today the endpoint is a fixed `Uri` ctor argument; `@endpoint` /`@hostLabel`
host prefixes and per-operation endpoints are unsupported.

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

**Proposed:** upgrade `SmithyRetryMiddleware` to standard mode:

- Exponential backoff `base * 2^(attempt-1)` with full jitter, capped (~20s).
- Token bucket (throttling errors cost more; successes refund) shared per client.
- Honor `Retry-After` as a minimum delay.
- Classify retryability: `@retryable` (with `throttling`), transient transport
  failures, 429, and 5xx.

## Auth follow-ups

Service-level priority resolution shipped (`SmithyAuthSchemeResolver`,
`ISmithyAuthScheme.SchemeId`, codegen `ModeledAuthSchemes` via
`ServiceIndex.getEffectiveAuthSchemes`). Remaining, in rough priority:

- Per-operation `@auth` override (needs per-operation resolution; pairs with the
  pipeline being built once at construction today).
- Identity/signer separation (smithy-java's `Identity` / `IdentityResolver` /
  `Signer`) so one credential set can feed multiple schemes — deferred; today
  credentials are baked into the scheme.
- Endpoint-driven auth override (see Gap 2, step 3).
- SigV4: pin a golden vector from AWS's `sig-v4-test-suite`, and revisit the
  always-on `X-Amz-Content-Sha256` header (an S3-ism; affects which canonical
  vectors validate) and `UNSIGNED-PAYLOAD` for streaming bodies.
