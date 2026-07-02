using System.Globalization;
using System.Net;
using NSmithy.Core;
using NSmithy.Http;

namespace NSmithy.Client;

/// <summary>
/// The recommended retry strategy: exponential backoff with full jitter and a backoff cap, a
/// client-shared retry quota, and <c>Retry-After</c> support. Retryability comes from transport
/// failures, modeled <c>@retryable</c> errors (<see cref="ISmithyRetryableError"/>), and
/// transient HTTP status codes. Throttling outcomes back off from a larger base delay.
/// </summary>
public sealed class SmithyStandardRetryStrategy : ISmithyRetryStrategy
{
    private static readonly ContextKey<int> AcquiredQuota = new("smithy.retry.acquiredQuota");

    private static readonly TimeSpan DefaultBaseDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan DefaultThrottlingBaseDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan DefaultMaxDelay = TimeSpan.FromSeconds(20);

    private const int QuotaCapacity = 500;
    private const int RetryCost = 5;
    private const int TransportFailureRetryCost = 10;
    private const int SuccessRefund = 1;

    private readonly object quotaLock = new();
    private int quota = QuotaCapacity;

    private readonly Func<SmithyRetryOutcome, SmithyRetryVerdict>? classifyOutcome;
    private readonly TimeProvider timeProvider;
    private readonly Random random;

    public SmithyStandardRetryStrategy(
        int maxAttempts = 3,
        TimeSpan? baseDelay = null,
        TimeSpan? maxDelay = null,
        Func<SmithyRetryOutcome, SmithyRetryVerdict>? classifyOutcome = null,
        TimeProvider? timeProvider = null,
        Random? random = null
    )
    {
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxAttempts),
                maxAttempts,
                "Retry attempts must be greater than zero."
            );
        }

        if (baseDelay is { } configuredBase && configuredBase <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(baseDelay),
                configuredBase,
                "Base delay must be positive."
            );
        }

        if (maxDelay is { } configuredMax && configuredMax <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDelay),
                configuredMax,
                "Max delay must be positive."
            );
        }

        MaxAttempts = maxAttempts;
        BaseDelay = baseDelay ?? DefaultBaseDelay;
        ThrottlingBaseDelay =
            baseDelay is { } custom && custom > DefaultThrottlingBaseDelay
                ? custom
                : DefaultThrottlingBaseDelay;
        MaxDelay = maxDelay ?? DefaultMaxDelay;
        this.classifyOutcome = classifyOutcome;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.random = random ?? Random.Shared;
    }

    public int MaxAttempts { get; }

    public TimeSpan BaseDelay { get; }

    public TimeSpan ThrottlingBaseDelay { get; }

    public TimeSpan MaxDelay { get; }

    public SmithyRetryDecision Classify(SmithyRetryOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        if (outcome.Attempt >= MaxAttempts)
        {
            return SmithyRetryDecision.GiveUp;
        }

        var verdict = classifyOutcome?.Invoke(outcome) ?? DefaultClassify(outcome);
        if (verdict == SmithyRetryVerdict.NotRetryable)
        {
            return SmithyRetryDecision.GiveUp;
        }

        var cost = outcome.IsTransportFailure ? TransportFailureRetryCost : RetryCost;
        if (!TryAcquireQuota(outcome.ExecutionContext, cost))
        {
            return SmithyRetryDecision.GiveUp;
        }

        return SmithyRetryDecision.RetryAfter(
            RetryAfterDelay(outcome.Response) ?? BackoffDelay(outcome.Attempt, verdict)
        );
    }

    public void RecordSuccess(SmithyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Refund what this execution's retries consumed; a first-attempt success slowly
        // replenishes quota spent by other executions.
        var refund = context.TryGet(AcquiredQuota, out var acquired) ? acquired : SuccessRefund;
        lock (quotaLock)
        {
            quota = Math.Min(QuotaCapacity, quota + refund);
        }
    }

    private static SmithyRetryVerdict DefaultClassify(SmithyRetryOutcome outcome)
    {
        if (outcome.IsTransportFailure)
        {
            return SmithyRetryVerdict.Retryable;
        }

        if (outcome.Error is ISmithyRetryableError retryableError)
        {
            return retryableError.IsThrottlingError
                ? SmithyRetryVerdict.Throttling
                : SmithyRetryVerdict.Retryable;
        }

        return outcome.Response!.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => SmithyRetryVerdict.Throttling,
            HttpStatusCode.RequestTimeout => SmithyRetryVerdict.Retryable,
            HttpStatusCode.InternalServerError => SmithyRetryVerdict.Retryable,
            HttpStatusCode.BadGateway => SmithyRetryVerdict.Retryable,
            HttpStatusCode.ServiceUnavailable => SmithyRetryVerdict.Retryable,
            HttpStatusCode.GatewayTimeout => SmithyRetryVerdict.Retryable,
            _ => SmithyRetryVerdict.NotRetryable,
        };
    }

    private bool TryAcquireQuota(SmithyContext context, int cost)
    {
        lock (quotaLock)
        {
            if (quota < cost)
            {
                return false;
            }

            quota -= cost;
        }

        var acquired = context.TryGet(AcquiredQuota, out var existing) ? existing : 0;
        context.Set(AcquiredQuota, acquired + cost);
        return true;
    }

    private TimeSpan BackoffDelay(int attempt, SmithyRetryVerdict verdict)
    {
        var baseDelay = verdict == SmithyRetryVerdict.Throttling ? ThrottlingBaseDelay : BaseDelay;
        var exponentialMs = baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var cappedMs = Math.Min(MaxDelay.TotalMilliseconds, exponentialMs);
        double sample;
        lock (quotaLock)
        {
            sample = random.NextDouble();
        }

        return TimeSpan.FromMilliseconds(cappedMs * sample);
    }

    private TimeSpan? RetryAfterDelay(SmithyHttpResponse? response)
    {
        if (
            response is null
            || !response.Headers.TryGetValue("Retry-After", out var values)
            || values.Count == 0
        )
        {
            return null;
        }

        var value = values[0];
        if (
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            && seconds >= 0
        )
        {
            return TimeSpan.FromSeconds(seconds);
        }

        if (
            DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var date
            )
        )
        {
            var delay = date - timeProvider.GetUtcNow();
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return null;
    }
}

/// <summary>How a retry strategy classified one failed attempt.</summary>
public enum SmithyRetryVerdict
{
    NotRetryable,
    Retryable,

    /// <summary>Retryable, but the service is shedding load; back off from a larger base delay.</summary>
    Throttling,
}
