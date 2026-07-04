using System.Net;

namespace NSmithy.Client;

/// <summary>
/// A minimal retry strategy: a fixed attempt budget, an optional fixed delay, and a
/// pluggable retry predicate. By default it retries transport failures, HTTP 429, and 5xx
/// responses. For production use prefer a strategy with backoff and jitter.
/// </summary>
public sealed class SmithySimpleRetryStrategy : ISmithyRetryStrategy, ISmithyRetrySession
{
    private readonly Func<SmithyRetryOutcome, bool> shouldRetry;

    public SmithySimpleRetryStrategy(
        int maxAttempts = 3,
        TimeSpan? delay = null,
        Func<SmithyRetryOutcome, bool>? shouldRetry = null
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

        MaxAttempts = maxAttempts;
        Delay = delay ?? TimeSpan.Zero;
        this.shouldRetry = shouldRetry ?? DefaultShouldRetry;
    }

    public int MaxAttempts { get; }

    public TimeSpan Delay { get; }

    // Stateless: every execution shares this instance as its session.
    public ISmithyRetrySession Begin() => this;

    public SmithyRetryDecision Classify(SmithyRetryOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return outcome.Attempt < MaxAttempts && shouldRetry(outcome)
            ? SmithyRetryDecision.RetryAfter(Delay)
            : SmithyRetryDecision.GiveUp;
    }

    private static bool DefaultShouldRetry(SmithyRetryOutcome outcome) =>
        outcome.Response is null
        || outcome.Response.StatusCode == HttpStatusCode.TooManyRequests
        || (int)outcome.Response.StatusCode is >= 500 and <= 599;
}
