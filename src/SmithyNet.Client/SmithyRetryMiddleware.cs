using System.Net;

namespace SmithyNet.Client;

public sealed class SmithyRetryMiddleware : ISmithyClientMiddleware
{
    private readonly int maxAttempts;
    private readonly TimeSpan delay;

    public SmithyRetryMiddleware(int maxAttempts = 3, TimeSpan? delay = null)
    {
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxAttempts),
                maxAttempts,
                "Retry attempts must be greater than zero."
            );
        }

        this.maxAttempts = maxAttempts;
        this.delay = delay ?? TimeSpan.Zero;
    }

    public async Task<SmithyOperationResponse> InvokeAsync(
        SmithyOperationRequest request,
        SmithyOperationNext nextOperation,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(nextOperation);

        for (var attempt = 1; ; attempt++)
        {
            var response = await nextOperation(request, cancellationToken).ConfigureAwait(false);
            if (attempt >= maxAttempts || !ShouldRetry(response))
            {
                return response;
            }

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool ShouldRetry(SmithyOperationResponse response)
    {
        return response.Response.StatusCode == HttpStatusCode.TooManyRequests
            || (int)response.Response.StatusCode is >= 500 and <= 599;
    }
}
