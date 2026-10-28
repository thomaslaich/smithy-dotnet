using NSmithy.Client;

namespace NSmithy.Tests.Client;

public sealed class SmithySmokeTestTests
{
    [Fact]
    public async Task RunDisposesTheClientItCreated()
    {
        var client = new FakeClient();
        var test = Test(SmithySmokeTestExpectation.Success, _ => Task.CompletedTask, client);

        var result = await test.RunAsync(new Config());

        Assert.True(result.Passed);
        Assert.True(client.Disposed);
    }

    [Fact]
    public async Task CancellationThroughTheCallersTokenPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var test = Test(
            SmithySmokeTestExpectation.Failure(null, static _ => true),
            ct => Task.FromCanceled(ct),
            new FakeClient()
        );

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            test.RunAsync(new Config(), cancellation.Token)
        );
    }

    [Fact]
    public async Task AnUnexpectedExceptionFailsASuccessTestWithItsMessage()
    {
        var test = Test(
            SmithySmokeTestExpectation.Success,
            _ => throw new InvalidOperationException("boom"),
            new FakeClient()
        );

        var result = await test.RunAsync(new Config());

        Assert.False(result.Passed);
        Assert.Contains(
            "InvalidOperationException: boom",
            result.Failure,
            StringComparison.Ordinal
        );
        Assert.IsType<InvalidOperationException>(result.Exception);
    }

    [Fact]
    public async Task RunConfiguresACopyBeforeCreatingTheClient()
    {
        Config? seen = null;
        var given = new Config();
        var test = new SmithySmokeTest<Config, FakeClient>(
            "Test",
            "Operation",
            SmithySmokeTestExpectation.Success,
            [],
            static config => new Config { Region = "us-west-2" },
            config =>
            {
                seen = config;
                return new FakeClient();
            },
            static (_, _) => Task.CompletedTask
        );

        await test.RunAsync(given);

        Assert.Equal("us-west-2", seen?.Region);
        Assert.Null(given.Region);
    }

    private static SmithySmokeTest<Config, FakeClient> Test(
        SmithySmokeTestExpectation expectation,
        Func<CancellationToken, Task> invoke,
        FakeClient client
    ) =>
        new(
            "Test",
            "Operation",
            expectation,
            [],
            static config => config,
            _ => client,
            (_, ct) => invoke(ct)
        );

    private sealed class Config
    {
        public string? Region { get; init; }
    }

    private sealed class FakeClient : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
