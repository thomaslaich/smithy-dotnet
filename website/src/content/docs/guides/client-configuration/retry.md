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
        RetryStrategy = new SmithySimpleRetryStrategy(maxAttempts: 3),
    });
```

`null` disables runtime retries. `SmithySimpleRetryStrategy` retries HTTP 429
and 5xx responses, with an optional fixed delay:

```csharp
RetryStrategy = new SmithySimpleRetryStrategy(
    maxAttempts: 3,
    delay: TimeSpan.FromMilliseconds(100));
```

For custom behavior, implement `ISmithyRetryStrategy`:

```csharp
public sealed class BackoffRetryStrategy : ISmithyRetryStrategy
{
    public int MaxAttempts => 4;

    public bool ShouldRetry(SmithyRetryContext context) =>
        context.Response.StatusCode == HttpStatusCode.TooManyRequests
        || (int)context.Response.StatusCode is >= 500 and <= 599;

    public async ValueTask DelayAsync(
        SmithyRetryContext context,
        CancellationToken cancellationToken = default)
    {
        var delay = TimeSpan.FromMilliseconds(100 * Math.Pow(2, context.Attempt - 1));
        await Task.Delay(delay, cancellationToken);
    }
}
```

Retries run inside the NSmithy client runtime. Request interceptors run again for
each retry attempt, and each attempt starts from the serialized request rather
than a previously mutated request.

Streaming request bodies are not retried by the runtime because they cannot be
replayed safely in the general case.
