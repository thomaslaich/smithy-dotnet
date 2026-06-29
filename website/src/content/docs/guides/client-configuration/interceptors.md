---
title: Interceptors
description: Observe and modify generated client execution.
---

Interceptors are the primary extension point for generated clients. Add them to
`{Service}ClientConfig.Interceptors`:

```csharp
var client = new WeatherClient(
    new Uri("https://api.example.com"),
    new()
    {
        Interceptors = { new CorrelationIdInterceptor() },
    });
```

Implement `IClientInterceptor` and override only the hooks you need:

```csharp
public sealed class CorrelationIdInterceptor : IClientInterceptor
{
    public ValueTask<SmithyHttpRequest> OnBeforeTransmitAsync(
        SmithyContext context,
        SmithyHttpRequest request,
        CancellationToken cancellationToken = default)
    {
        request.Headers["X-Correlation-Id"] = Guid.NewGuid().ToString("N");
        return ValueTask.FromResult(request);
    }
}
```

Available hooks:

| Hook | Use |
| --- | --- |
| `OnBeforeExecution` | Read or initialize per-call context before runtime work starts. |
| `OnBeforeSerialization` | Observe the typed input before protocol serialization. |
| `OnBeforeSigningAsync` | Modify the serialized request before auth signing. |
| `OnBeforeTransmitAsync` | Modify the signed request before transport sends it. |
| `OnAfterTransmit` | Observe the raw response before deserialization. |
| `OnAfterDeserialization` | Observe the typed output after protocol deserialization. |
| `OnAfterExecution` | Run cleanup or final observation after the call completes. |

Before hooks run in configured order. After hooks run in reverse order.

`ISmithyClientMiddleware` remains available for compatibility with older
send-stage extensions, but new client extensions should use interceptors.
