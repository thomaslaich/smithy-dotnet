using System.Net;
using NSmithy.Http;

namespace NSmithy.Client;

public sealed class SmithyRetryStrategy
{
    private readonly Func<SmithyHttpResponse, bool> shouldRetry;

    public SmithyRetryStrategy(
        int maxAttempts = 3,
        TimeSpan? delay = null,
        Func<SmithyHttpResponse, bool>? shouldRetry = null
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

    public bool ShouldRetry(SmithyHttpResponse response) => shouldRetry(response);

    public ValueTask DelayAsync(int attempt, CancellationToken cancellationToken = default) =>
        Delay > TimeSpan.Zero
            ? new ValueTask(Task.Delay(Delay, cancellationToken))
            : ValueTask.CompletedTask;

    private static bool DefaultShouldRetry(SmithyHttpResponse response) =>
        response.StatusCode == HttpStatusCode.TooManyRequests
        || (int)response.StatusCode is >= 500 and <= 599;
}
