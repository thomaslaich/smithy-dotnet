using System.Net;
using NSmithy.Http;

namespace NSmithy.Client;

public sealed class SmithySimpleRetryStrategy : ISmithyRetryStrategy
{
    private readonly Func<SmithyRetryContext, bool> shouldRetry;

    public SmithySimpleRetryStrategy(
        int maxAttempts = 3,
        TimeSpan? delay = null,
        Func<SmithyRetryContext, bool>? shouldRetry = null
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

    public bool ShouldRetry(SmithyRetryContext context) => shouldRetry(context);

    public ValueTask DelayAsync(
        SmithyRetryContext context,
        CancellationToken cancellationToken = default
    ) =>
        Delay > TimeSpan.Zero
            ? new ValueTask(Task.Delay(Delay, cancellationToken))
            : ValueTask.CompletedTask;

    private static bool DefaultShouldRetry(SmithyRetryContext context) =>
        context.Response.StatusCode == HttpStatusCode.TooManyRequests
        || (int)context.Response.StatusCode is >= 500 and <= 599;
}
