using NSmithy.Client;
using NSmithy.Core;

namespace NSmithy.Tests.Client;

public sealed class SmithyCachingIdentityResolverTests
{
    private static readonly SmithyIdentityProperties Properties = new(
        ShapeId.Parse("example#Service"),
        ShapeId.Parse("example#Operation"),
        new SmithyEndpoint(new Uri("https://api.example.com"))
    );

    [Fact]
    public async Task ReusesIdentityUntilRefreshWindow()
    {
        var now = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        var time = new MutableTimeProvider(now);
        var inner = new QueueResolver(
            new TestIdentity("one", now.AddMinutes(10)),
            new TestIdentity("two", now.AddHours(1))
        );
        var resolver = new SmithyCachingIdentityResolver(
            inner,
            refreshBuffer: TimeSpan.FromMinutes(2),
            timeProvider: time
        );

        var first = await resolver.ResolveIdentityAsync(Properties);
        var cached = await resolver.ResolveIdentityAsync(Properties);

        Assert.Same(first, cached);
        Assert.Equal(1, inner.CallCount);

        time.UtcNow = now.AddMinutes(9);
        var refreshed = await resolver.ResolveIdentityAsync(Properties);

        Assert.Equal("two", Assert.IsType<TestIdentity>(refreshed).Value);
        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task ConcurrentColdCallsShareOneRefresh()
    {
        var inner = new BlockingResolver();
        var resolver = new SmithyCachingIdentityResolver(inner);

        var calls = Enumerable
            .Range(0, 8)
            .Select(_ => resolver.ResolveIdentityAsync(Properties).AsTask())
            .ToArray();
        var identity = new TestIdentity("shared", Expiration: null);
        inner.Complete(identity);

        var resolved = await Task.WhenAll(calls);

        Assert.Equal(1, inner.CallCount);
        Assert.All(resolved, value => Assert.Same(identity, value));
    }

    private sealed record TestIdentity(string Value, DateTimeOffset? Expiration) : ISmithyIdentity;

    private sealed class QueueResolver(params TestIdentity[] identities) : ISmithyIdentityResolver
    {
        private readonly Queue<TestIdentity> identities = new(identities);

        public int CallCount { get; private set; }

        public ValueTask<ISmithyIdentity> ResolveIdentityAsync(
            SmithyIdentityProperties properties,
            CancellationToken cancellationToken = default
        )
        {
            CallCount++;
            return ValueTask.FromResult<ISmithyIdentity>(identities.Dequeue());
        }
    }

    private sealed class BlockingResolver : ISmithyIdentityResolver
    {
        private readonly TaskCompletionSource<ISmithyIdentity> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public int CallCount { get; private set; }

        public async ValueTask<ISmithyIdentity> ResolveIdentityAsync(
            SmithyIdentityProperties properties,
            CancellationToken cancellationToken = default
        )
        {
            CallCount++;
            return await completion.Task.ConfigureAwait(false);
        }

        public void Complete(ISmithyIdentity identity) => completion.SetResult(identity);
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
