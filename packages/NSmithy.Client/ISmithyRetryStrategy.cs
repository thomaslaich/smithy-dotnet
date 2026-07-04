using NSmithy.Http;

namespace NSmithy.Client;

/// <summary>
/// Decides whether the runtime retries failed attempts. A strategy is long-lived and shared by
/// every execution of its client (it owns client-wide state such as a retry quota) and must be
/// thread-safe. <see cref="Begin"/> is called once at the start of each operation execution and
/// returns that execution's <see cref="ISmithyRetrySession"/>.
/// </summary>
public interface ISmithyRetryStrategy
{
    ISmithyRetrySession Begin();
}

/// <summary>
/// One operation execution's view of a retry strategy. The runtime calls
/// <see cref="Classify"/> after each failed attempt and <see cref="RecordSuccess"/> once if an
/// attempt succeeds. A session is used by a single execution and does not need to be
/// thread-safe; stateless strategies may return themselves from
/// <see cref="ISmithyRetryStrategy.Begin"/>.
/// </summary>
public interface ISmithyRetrySession
{
    SmithyRetryDecision Classify(SmithyRetryOutcome outcome);

    /// <summary>
    /// Called when an attempt succeeds. Quota-based sessions use this to refund tokens acquired
    /// by this execution's earlier retries.
    /// </summary>
    void RecordSuccess() { }
}

/// <summary>
/// The result of one failed attempt.
/// </summary>
/// <param name="Attempt">The 1-based attempt number that produced this outcome.</param>
/// <param name="Response">
/// The received response, or null when the attempt failed in transport before a response arrived.
/// </param>
/// <param name="Error">
/// The exception the runtime will throw if the attempt is not retried: the transport exception
/// when <paramref name="Response"/> is null, otherwise the deserialized modeled error (or a
/// <see cref="SmithyClientException"/> when no modeled error matched).
/// </param>
/// <param name="ExecutionContext">The invocation's execution context.</param>
public sealed record SmithyRetryOutcome(
    int Attempt,
    SmithyHttpResponse? Response,
    Exception Error,
    SmithyContext ExecutionContext
)
{
    /// <summary>True when the attempt failed before a response arrived.</summary>
    public bool IsTransportFailure => Response is null;
}

/// <summary>
/// A retry strategy's verdict on a failed attempt: give up (the runtime throws the outcome's
/// error) or retry after a delay.
/// </summary>
public readonly struct SmithyRetryDecision : IEquatable<SmithyRetryDecision>
{
    private SmithyRetryDecision(bool shouldRetry, TimeSpan delay)
    {
        ShouldRetry = shouldRetry;
        Delay = delay;
    }

    public static SmithyRetryDecision GiveUp => default;

    public static SmithyRetryDecision RetryAfter(TimeSpan delay) =>
        delay >= TimeSpan.Zero
            ? new SmithyRetryDecision(true, delay)
            : throw new ArgumentOutOfRangeException(
                nameof(delay),
                delay,
                "Retry delay must not be negative."
            );

    public bool ShouldRetry { get; }

    public TimeSpan Delay { get; }

    public bool Equals(SmithyRetryDecision other) =>
        ShouldRetry == other.ShouldRetry && Delay == other.Delay;

    public override bool Equals(object? obj) => obj is SmithyRetryDecision other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(ShouldRetry, Delay);

    public static bool operator ==(SmithyRetryDecision left, SmithyRetryDecision right) =>
        left.Equals(right);

    public static bool operator !=(SmithyRetryDecision left, SmithyRetryDecision right) =>
        !left.Equals(right);
}
