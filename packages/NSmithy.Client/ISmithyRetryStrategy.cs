using NSmithy.Http;

namespace NSmithy.Client;

public interface ISmithyRetryStrategy
{
    int MaxAttempts { get; }

    bool ShouldRetry(SmithyRetryContext context);

    ValueTask DelayAsync(SmithyRetryContext context, CancellationToken cancellationToken = default);
}

public sealed record SmithyRetryContext(
    int Attempt,
    SmithyHttpResponse Response,
    SmithyContext ExecutionContext
);
