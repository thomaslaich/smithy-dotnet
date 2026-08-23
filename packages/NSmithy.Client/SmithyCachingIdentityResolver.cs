namespace NSmithy.Client;

/// <summary>
/// Caches identities returned by another resolver and performs a single shared refresh when the
/// cached identity is absent or close to expiration.
/// </summary>
public sealed class SmithyCachingIdentityResolver : ISmithyIdentityResolver
{
    private static readonly TimeSpan DefaultRefreshBuffer = TimeSpan.FromMinutes(5);

    private readonly ISmithyIdentityResolver inner;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan refreshBuffer;
    private readonly object sync = new();
    private ISmithyIdentity? cachedIdentity;
    private Task<ISmithyIdentity>? refreshTask;

    public SmithyCachingIdentityResolver(
        ISmithyIdentityResolver inner,
        TimeSpan? refreshBuffer = null,
        TimeProvider? timeProvider = null
    )
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.refreshBuffer = refreshBuffer ?? DefaultRefreshBuffer;
        if (this.refreshBuffer < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(refreshBuffer),
                refreshBuffer,
                "The refresh buffer cannot be negative."
            );
        }

        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ValueTask<ISmithyIdentity> ResolveIdentityAsync(
        SmithyIdentityProperties properties,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(properties);
        cancellationToken.ThrowIfCancellationRequested();

        lock (sync)
        {
            if (IsFresh(cachedIdentity))
            {
                return ValueTask.FromResult(cachedIdentity!);
            }

            var pendingRefresh = refreshTask;
            if (pendingRefresh is null)
            {
                var completion = new TaskCompletionSource<ISmithyIdentity>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                pendingRefresh = completion.Task;
                refreshTask = pendingRefresh;
                _ = RefreshAsync(properties, completion);
            }

            return new ValueTask<ISmithyIdentity>(pendingRefresh.WaitAsync(cancellationToken));
        }
    }

    private bool IsFresh(ISmithyIdentity? identity) =>
        identity is not null
        && (
            identity.Expiration is null
            || identity.Expiration.Value - timeProvider.GetUtcNow() > refreshBuffer
        );

    private async Task RefreshAsync(
        SmithyIdentityProperties properties,
        TaskCompletionSource<ISmithyIdentity> completion
    )
    {
        try
        {
            // The refresh is shared by concurrent callers. Individual callers can stop waiting,
            // but one cancellation must not cancel credentials needed by every other caller.
            var identity = await inner
                .ResolveIdentityAsync(properties, CancellationToken.None)
                .ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(identity);

            lock (sync)
            {
                cachedIdentity = identity;
            }

            completion.TrySetResult(identity);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
        finally
        {
            lock (sync)
            {
                refreshTask = null;
            }
        }
    }
}
