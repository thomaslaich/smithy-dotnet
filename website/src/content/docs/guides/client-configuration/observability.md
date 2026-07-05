---
title: Observability
description: OpenTelemetry tracing and metrics for generated clients.
---

The client runtime emits OpenTelemetry-friendly telemetry through the standard
.NET primitives — no NSmithy-specific setup, no extra packages. Subscribe with
your tracer/meter provider:

```csharp
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource("NSmithy.Client"))
    .WithMetrics(metrics => metrics.AddMeter("NSmithy.Client"));
```

The source and meter names are also available as constants:
`SmithyClientTelemetry.ActivitySourceName` and
`SmithyClientTelemetry.MeterName`.

## Traces

Each operation execution produces a client span named
`{Service}.{Operation}` (e.g. `Weather.GetForecast`), with one child span per
transport attempt, so retries are visible in the trace:

| Attribute | Value |
| --- | --- |
| `rpc.system` | `smithy` |
| `rpc.service` | The service shape id (e.g. `example.weather#Weather`). |
| `rpc.method` | The operation name. |
| `error.type` | The exception type on failure (execution span). |
| `smithy.attempt` | The 1-based attempt number (attempt spans). |

Failed executions set the span status to `Error` with the exception message;
failed attempts mark their attempt span the same way, so a retried-then-
successful call shows a failed first attempt under a successful operation span.

## Metrics

| Instrument | Type | Unit | Meaning |
| --- | --- | --- | --- |
| `smithy.client.operation.duration` | Histogram | `s` | End-to-end operation duration, spanning all attempts and backoff. |
| `smithy.client.attempts` | Counter | `{attempt}` | Total transport attempts, including retries. |
| `smithy.client.errors` | Counter | `{error}` | Failed operation executions, dimensioned by `error.type`. |

All instruments carry `rpc.system`, `rpc.service`, and `rpc.method` dimensions.
The ratio of `smithy.client.attempts` to operation count is a direct
retry-amplification signal.

For custom logging or per-request diagnostics beyond spans and metrics, use
[interceptors](/smithy-dotnet/guides/client-configuration/interceptors/) —
`OnAfterExecution` receives the outcome (the exception, or `null` on success).
