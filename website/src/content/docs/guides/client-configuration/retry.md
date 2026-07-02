---
title: Retry
description: Configure runtime-owned retries for generated clients.
---

Generated clients can retry failed unary operations through
`{Service}ClientConfig.RetryStrategy`:

```csharp
var client = new WeatherClient(
    new Uri("https://api.example.com"),
    new()
    {
        RetryStrategy = new SmithyStandardRetryStrategy(maxAttempts: 3),
    });
```

`null` disables runtime retries.

## Standard strategy

`SmithyStandardRetryStrategy` is the recommended strategy. It retries transport
failures, modeled `@retryable` errors, and transient HTTP status codes (408,
429, 500, 502, 503, 504), using:

- exponential backoff with full jitter and a backoff cap (default base 100 ms,
  cap 20 s; throttling outcomes back off from a 500 ms base)
- a client-shared retry quota, so a downstream outage cannot amplify traffic
  through unbounded retries
- the server's `Retry-After` header (seconds or HTTP-date) when present

```csharp
RetryStrategy = new SmithyStandardRetryStrategy(
    maxAttempts: 5,
    maxDelay: TimeSpan.FromSeconds(10));
```

Errors modeled with `@retryable` implement `ISmithyRetryableError`, so modeled
retryability works regardless of protocol or status code —
`@retryable(throttling: true)` errors are treated as throttling.

## Simple strategy

`SmithySimpleRetryStrategy` retries transport failures, HTTP 429, and 5xx
responses, with an optional fixed delay and no backoff, jitter, or quota —
useful in tests:

```csharp
RetryStrategy = new SmithySimpleRetryStrategy(
    maxAttempts: 3,
    delay: TimeSpan.FromMilliseconds(100));
```

## Custom strategies

For custom behavior, implement `ISmithyRetryStrategy`. The runtime classifies
each failed attempt — deserializing the modeled error when there is one — and
passes the outcome to the strategy. The outcome carries the attempt number, the
response (`null` for transport failures), and the exception that will propagate
to the caller if the attempt is not retried:

```csharp
public sealed class BackoffRetryStrategy : ISmithyRetryStrategy
{
    public SmithyRetryDecision Classify(SmithyRetryOutcome outcome)
    {
        if (outcome.Attempt >= 4 || !IsTransient(outcome))
        {
            return SmithyRetryDecision.GiveUp;
        }

        var delay = TimeSpan.FromMilliseconds(100 * Math.Pow(2, outcome.Attempt - 1));
        return SmithyRetryDecision.RetryAfter(delay);
    }

    private static bool IsTransient(SmithyRetryOutcome outcome) =>
        outcome.IsTransportFailure
        || outcome.Response!.StatusCode == HttpStatusCode.TooManyRequests
        || (int)outcome.Response.StatusCode is >= 500 and <= 599;
}
```

Because the modeled error is deserialized before the retry decision,
`outcome.Error` lets a strategy retry on specific modeled error types (for
example, a service's throttling error).

Retries run inside the NSmithy client runtime. Request interceptors run again for
each retry attempt, and each attempt starts from the serialized request rather
than a previously mutated request.

Streaming request bodies are not retried by the runtime because they cannot be
replayed safely in the general case.
