using System.Net;
using NSmithy.Client;

namespace NSmithy.Tests.Client;

public sealed class SmithyWaiterTests
{
    private static readonly TimeSpan MinDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(120);

    private static readonly SmithyWaiterAcceptor<string, string>[] Acceptors =
    [
        new(SmithyWaiterState.Success, static (_, output, _) => output == "ready"),
        new(SmithyWaiterState.Failure, static (_, output, _) => output == "broken"),
        new(SmithyWaiterState.Success, static (_, _, error) => error is GoneException),
        new(SmithyWaiterState.Retry, static (_, _, error) => error is NotYetException),
    ];

    [Fact]
    public async Task SuccessOnTheFirstAttemptDoesNotWait()
    {
        var time = new RecordingTimeProvider();

        var result = await Wait(Script("ready"), time, TimeSpan.FromMinutes(5));

        Assert.Equal("ready", result.Output);
        Assert.Null(result.Exception);
        Assert.Equal(1, result.Attempts);
        Assert.Empty(time.Delays);
    }

    [Fact]
    public async Task RetriesOutputsNoAcceptorMatchesAndRetryAcceptors()
    {
        var time = new RecordingTimeProvider();

        var result = await Wait(
            Script("creating", new NotYetException(), "creating", "ready"),
            time,
            TimeSpan.FromMinutes(5)
        );

        Assert.Equal("ready", result.Output);
        Assert.Equal(4, result.Attempts);
        Assert.Equal(3, time.Delays.Count);
    }

    [Fact]
    public async Task AnErrorCanBeTheSuccessState()
    {
        var result = await Wait(
            Script("deleting", new GoneException()),
            new RecordingTimeProvider(),
            TimeSpan.FromMinutes(5)
        );

        Assert.IsType<GoneException>(result.Exception);
        Assert.Null(result.Output);
        Assert.Equal(2, result.Attempts);
    }

    [Fact]
    public async Task AFailureAcceptorFailsTheWaiter()
    {
        var error = await Assert.ThrowsAsync<SmithyWaiterFailureException>(() =>
            Wait(Script("creating", "broken"), new RecordingTimeProvider(), TimeSpan.FromMinutes(5))
        );

        Assert.Equal("Test", error.WaiterName);
        Assert.Equal(2, error.Attempts);
        Assert.Equal("broken", error.LastOutput);
        Assert.Null(error.LastException);
    }

    [Fact]
    public async Task AnErrorNoAcceptorMatchesFailsTheWaiter()
    {
        var unmatched = new SmithyClientException(HttpStatusCode.BadRequest, null, "Throttled");

        var error = await Assert.ThrowsAsync<SmithyWaiterFailureException>(() =>
            Wait(Script(unmatched), new RecordingTimeProvider(), TimeSpan.FromMinutes(5))
        );

        Assert.Same(unmatched, error.InnerException);
        Assert.Same(unmatched, error.LastException);
        Assert.Equal(1, error.Attempts);
    }

    [Fact]
    public async Task DelaysGrowExponentiallyWithinTheModeledBounds()
    {
        var time = new RecordingTimeProvider();

        await Assert.ThrowsAsync<SmithyWaiterTimeoutException>(() =>
            Wait(Forever("creating"), time, TimeSpan.FromHours(1))
        );

        Assert.Equal(MinDelay, time.Delays[0]);
        // The last delay is cut short so the minimum delay remains, and may be shorter than it.
        Assert.All(time.Delays[..^1], delay => Assert.InRange(delay, MinDelay, MaxDelay));
        Assert.InRange(time.Delays[^1], TimeSpan.FromTicks(1), MaxDelay);
        // Attempt n waits at most minDelay * 2^(n-1), until that passes maxDelay.
        for (var i = 0; i < 6; i++)
        {
            Assert.True(
                time.Delays[i] <= MinDelay * Math.Pow(2, i),
                $"delay {i}: {time.Delays[i]}"
            );
        }
    }

    [Fact]
    public async Task TimesOutWhenLessThanTheMinimumDelayRemains()
    {
        var time = new RecordingTimeProvider();

        var error = await Assert.ThrowsAsync<SmithyWaiterTimeoutException>(() =>
            Wait(Forever("creating"), time, TimeSpan.FromSeconds(30))
        );

        Assert.Equal("creating", error.LastOutput);
        // No delay runs past the point where less than the minimum delay remains.
        Assert.True(time.Elapsed <= TimeSpan.FromSeconds(30) - MinDelay, $"{time.Elapsed}");
        Assert.Equal(time.Delays.Count + 1, error.Attempts);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(999)]
    [InlineData(9_999)]
    [InlineData(10_000)]
    [InlineData(25_000)]
    public async Task AFractionalLastDelayStillWaitsOnTheClock(long ticks)
    {
        // A wait time just over the minimum delay leaves a final delay of a few ticks, which
        // Task.Delay alone would take as no delay and so never advance a test clock. Started on
        // the thread pool so a wait that never yields fails instead of hanging.
        var time = new RecordingTimeProvider();
        var wait = Task.Run(() =>
            Wait(Forever("creating"), time, MinDelay + TimeSpan.FromTicks(ticks))
        );

        await Assert.ThrowsAsync<SmithyWaiterTimeoutException>(() =>
            wait.WaitAsync(TimeSpan.FromSeconds(5))
        );
        // Rounded up to a whole millisecond, which leaves less than the minimum delay.
        var expected = TimeSpan.FromMilliseconds(
            Math.Ceiling((double)ticks / TimeSpan.TicksPerMillisecond)
        );
        Assert.Equal(expected, Assert.Single(time.Delays));
    }

    [Fact]
    public async Task OptionsOverrideTheModeledDelays()
    {
        var time = new RecordingTimeProvider();
        var options = new SmithyWaiterOptions(TimeSpan.FromMinutes(5))
        {
            MinDelay = TimeSpan.FromSeconds(5),
            MaxDelay = TimeSpan.FromSeconds(5),
            TimeProvider = time,
        };

        await SmithyWaiter.WaitAsync(
            "Test",
            Script("creating", "creating", "ready"),
            "input",
            Acceptors,
            MinDelay,
            MaxDelay,
            options
        );

        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)], time.Delays);
    }

    [Fact]
    public async Task CancellationStopsTheWaiter()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        Task<string> Call(string input, CancellationToken token)
        {
            if (++calls == 2)
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }
            return Task.FromResult("creating");
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SmithyWaiter.WaitAsync(
                "Test",
                Call,
                "input",
                Acceptors,
                MinDelay,
                MaxDelay,
                new SmithyWaiterOptions(TimeSpan.FromMinutes(5))
                {
                    TimeProvider = new RecordingTimeProvider(),
                },
                cancellation.Token
            )
        );
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task RejectsANonPositiveMaxWaitTime()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Wait(Script("ready"), new RecordingTimeProvider(), TimeSpan.Zero)
        );
    }

    private static Task<SmithyWaiterResult<string>> Wait(
        Func<string, CancellationToken, Task<string>> call,
        TimeProvider time,
        TimeSpan maxWaitTime
    ) =>
        SmithyWaiter.WaitAsync(
            "Test",
            call,
            "input",
            Acceptors,
            MinDelay,
            MaxDelay,
            new SmithyWaiterOptions(maxWaitTime) { TimeProvider = time }
        );

    /// <summary>One attempt per item: an output to return, or an exception to throw.</summary>
    private static Func<string, CancellationToken, Task<string>> Script(params object[] attempts)
    {
        var next = 0;
        return (_, _) =>
            attempts[next++] switch
            {
                Exception exception => Task.FromException<string>(exception),
                var output => Task.FromResult((string)output),
            };
    }

    private static Func<string, CancellationToken, Task<string>> Forever(string output) =>
        (_, _) => Task.FromResult(output);

    private sealed class GoneException : Exception;

    private sealed class NotYetException : Exception;

    /// <summary>
    /// A clock that moves only when a delay runs: each timer advances it by its due time and
    /// fires at once.
    /// </summary>
    private sealed class RecordingTimeProvider : TimeProvider
    {
        private long ticks;

        public List<TimeSpan> Delays { get; } = [];

        public TimeSpan Elapsed => TimeSpan.FromTicks(ticks);

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => ticks;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        )
        {
            Delays.Add(dueTime);
            ticks += dueTime.Ticks;
            ThreadPool.QueueUserWorkItem(_ => callback(state));
            return new NoTimer();
        }

        private sealed class NoTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose() { }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
