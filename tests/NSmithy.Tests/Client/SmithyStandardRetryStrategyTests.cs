using System.Net;
using NSmithy.Client;
using NSmithy.Core;
using NSmithy.Http;

namespace NSmithy.Tests.Client;

public sealed class SmithyStandardRetryStrategyTests
{
    [Fact]
    public void BackoffGrowsExponentiallyWithFullJitter()
    {
        var strategy = new SmithyStandardRetryStrategy(maxAttempts: 4, random: new MaxRandom());

        var first = strategy
            .Begin()
            .Classify(Outcome(attempt: 1, StatusCode: HttpStatusCode.BadGateway));
        var second = strategy
            .Begin()
            .Classify(Outcome(attempt: 2, StatusCode: HttpStatusCode.BadGateway));
        var third = strategy
            .Begin()
            .Classify(Outcome(attempt: 3, StatusCode: HttpStatusCode.BadGateway));

        Assert.Equal(TimeSpan.FromMilliseconds(100), first.Delay);
        Assert.Equal(TimeSpan.FromMilliseconds(200), second.Delay);
        Assert.Equal(TimeSpan.FromMilliseconds(400), third.Delay);
    }

    [Fact]
    public void JitterSamplesBelowTheExponentialCeiling()
    {
        var strategy = new SmithyStandardRetryStrategy(maxAttempts: 2, random: new HalfRandom());

        var decision = strategy
            .Begin()
            .Classify(Outcome(attempt: 1, StatusCode: HttpStatusCode.BadGateway));

        Assert.Equal(TimeSpan.FromMilliseconds(50), decision.Delay);
    }

    [Fact]
    public void BackoffIsCappedAtMaxDelay()
    {
        var strategy = new SmithyStandardRetryStrategy(
            maxAttempts: 30,
            maxDelay: TimeSpan.FromSeconds(1),
            random: new MaxRandom()
        );

        var decision = strategy
            .Begin()
            .Classify(Outcome(attempt: 20, StatusCode: HttpStatusCode.BadGateway));

        Assert.Equal(TimeSpan.FromSeconds(1), decision.Delay);
    }

    [Fact]
    public void ThrottlingBacksOffFromLargerBaseDelay()
    {
        var strategy = new SmithyStandardRetryStrategy(maxAttempts: 2, random: new MaxRandom());

        var decision = strategy
            .Begin()
            .Classify(Outcome(attempt: 1, StatusCode: HttpStatusCode.TooManyRequests));

        Assert.Equal(TimeSpan.FromMilliseconds(500), decision.Delay);
    }

    [Fact]
    public void RetryAfterSecondsOverridesBackoff()
    {
        var strategy = new SmithyStandardRetryStrategy(maxAttempts: 2, random: new MaxRandom());

        var decision = strategy
            .Begin()
            .Classify(
                Outcome(attempt: 1, StatusCode: HttpStatusCode.ServiceUnavailable, retryAfter: "7")
            );

        Assert.True(decision.ShouldRetry);
        Assert.Equal(TimeSpan.FromSeconds(7), decision.Delay);
    }

    [Fact]
    public void RetryAfterHttpDateUsesTimeProvider()
    {
        var now = new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
        var strategy = new SmithyStandardRetryStrategy(
            maxAttempts: 2,
            timeProvider: new FixedTimeProvider(now)
        );

        var decision = strategy
            .Begin()
            .Classify(
                Outcome(
                    attempt: 1,
                    StatusCode: HttpStatusCode.ServiceUnavailable,
                    retryAfter: now.AddSeconds(30).ToString("R")
                )
            );

        Assert.True(decision.ShouldRetry);
        Assert.Equal(TimeSpan.FromSeconds(30), decision.Delay);
    }

    [Fact]
    public void GivesUpAfterMaxAttempts()
    {
        var strategy = new SmithyStandardRetryStrategy(maxAttempts: 3);

        var decision = strategy
            .Begin()
            .Classify(Outcome(attempt: 3, StatusCode: HttpStatusCode.ServiceUnavailable));

        Assert.False(decision.ShouldRetry);
    }

    [Fact]
    public void DoesNotRetryNonTransientErrors()
    {
        var strategy = new SmithyStandardRetryStrategy(maxAttempts: 3);

        var decision = strategy
            .Begin()
            .Classify(Outcome(attempt: 1, StatusCode: HttpStatusCode.BadRequest));

        Assert.False(decision.ShouldRetry);
    }

    [Fact]
    public void RetriesTransportFailures()
    {
        var strategy = new SmithyStandardRetryStrategy(maxAttempts: 3, random: new MaxRandom());

        var decision = strategy
            .Begin()
            .Classify(
                new SmithyRetryOutcome(
                    1,
                    null,
                    new HttpRequestException("connection reset"),
                    new SmithyContext()
                )
            );

        Assert.True(decision.ShouldRetry);
    }

    [Fact]
    public void RetriesModeledRetryableErrorsRegardlessOfStatusCode()
    {
        var strategy = new SmithyStandardRetryStrategy(maxAttempts: 3, random: new MaxRandom());

        var decision = strategy
            .Begin()
            .Classify(
                Outcome(
                    attempt: 1,
                    StatusCode: HttpStatusCode.BadRequest,
                    error: new RetryableTestError(throttling: false)
                )
            );

        Assert.True(decision.ShouldRetry);
    }

    [Fact]
    public void ModeledThrottlingErrorsUseThrottlingBackoff()
    {
        var strategy = new SmithyStandardRetryStrategy(maxAttempts: 3, random: new MaxRandom());

        var decision = strategy
            .Begin()
            .Classify(
                Outcome(
                    attempt: 1,
                    StatusCode: HttpStatusCode.BadRequest,
                    error: new RetryableTestError(throttling: true)
                )
            );

        Assert.Equal(TimeSpan.FromMilliseconds(500), decision.Delay);
    }

    [Fact]
    public void QuotaExhaustionStopsRetries()
    {
        var strategy = new SmithyStandardRetryStrategy(maxAttempts: 3);

        // Capacity 500, 5 tokens per response retry: 100 retries drain the bucket.
        for (var i = 0; i < 100; i++)
        {
            var decision = strategy
                .Begin()
                .Classify(Outcome(attempt: 1, StatusCode: HttpStatusCode.ServiceUnavailable));
            Assert.True(decision.ShouldRetry);
        }

        Assert.False(
            strategy
                .Begin()
                .Classify(Outcome(attempt: 1, StatusCode: HttpStatusCode.ServiceUnavailable))
                .ShouldRetry
        );
    }

    [Fact]
    public void RecordSuccessRefundsQuotaAcquiredByTheExecution()
    {
        var strategy = new SmithyStandardRetryStrategy(maxAttempts: 3);

        // Drain the bucket, remembering each execution's session.
        List<ISmithyRetrySession> sessions = [];
        for (var i = 0; i < 100; i++)
        {
            var session = strategy.Begin();
            var decision = session.Classify(
                Outcome(attempt: 1, StatusCode: HttpStatusCode.ServiceUnavailable)
            );
            Assert.True(decision.ShouldRetry);
            sessions.Add(session);
        }

        // A retried execution eventually succeeding refunds its tokens.
        sessions[0].RecordSuccess();

        Assert.True(
            strategy
                .Begin()
                .Classify(Outcome(attempt: 1, StatusCode: HttpStatusCode.ServiceUnavailable))
                .ShouldRetry
        );
    }

    [Fact]
    public void CustomClassifierOverridesDefault()
    {
        var strategy = new SmithyStandardRetryStrategy(
            maxAttempts: 3,
            classifyOutcome: static _ => SmithyRetryVerdict.NotRetryable
        );

        var decision = strategy
            .Begin()
            .Classify(Outcome(attempt: 1, StatusCode: HttpStatusCode.ServiceUnavailable));

        Assert.False(decision.ShouldRetry);
    }

    private static SmithyRetryOutcome Outcome(
        int attempt,
        HttpStatusCode StatusCode,
        string? retryAfter = null,
        Exception? error = null
    )
    {
        var response = Response(StatusCode, retryAfter);
        return new SmithyRetryOutcome(
            attempt,
            response,
            error ?? new SmithyClientException(StatusCode, null),
            new SmithyContext()
        );
    }

    private static SmithyHttpClientResponse Response(
        HttpStatusCode statusCode,
        string? retryAfter = null
    )
    {
        var headers = new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase
        );
        if (retryAfter is not null)
        {
            headers["Retry-After"] = [retryAfter];
        }

        return new SmithyHttpClientResponse(
            statusCode,
            statusCode.ToString(),
            [],
            headers,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        );
    }

    private sealed class MaxRandom : Random
    {
        public override double NextDouble() => 1.0;
    }

    private sealed class HalfRandom : Random
    {
        public override double NextDouble() => 0.5;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RetryableTestError(bool throttling) : Exception, ISmithyRetryableError
    {
        public bool IsThrottlingError { get; } = throttling;
    }
}
