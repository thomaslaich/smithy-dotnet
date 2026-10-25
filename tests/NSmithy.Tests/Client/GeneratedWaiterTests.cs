using System.Net;
using NSmithy.Client;
using Nsmithy.Tests.Waiters;

namespace NSmithy.Tests.Client;

/// <summary>
/// Runs the waiters generated from <c>model/waiters.smithy</c> against a fake client that returns a
/// scripted sequence of outputs and errors.
/// </summary>
public sealed class GeneratedWaiterTests
{
    private static readonly DescribeTableInput Input = new("orders");

    [Fact]
    public async Task TableExistsRetriesUntilTheTableIsActive()
    {
        var client = new ScriptedClient(
            new ResourceNotFoundException(),
            Table(TableStatus.CREATING),
            Table(TableStatus.ACTIVE)
        );

        var result = await client.WaitUntilTableExistsAsync(Input, Options());

        Assert.Equal(TableStatus.ACTIVE, result.Output?.Table?.TableStatus);
        Assert.Equal(3, result.Attempts);
    }

    [Fact]
    public async Task TableExistsFailsWhenTheTableFails()
    {
        var client = new ScriptedClient(Table(TableStatus.CREATING), Table(TableStatus.FAILED));

        var error = await Assert.ThrowsAsync<SmithyWaiterFailureException>(() =>
            client.WaitUntilTableExistsAsync(Input, Options())
        );

        Assert.Equal("TableExists", error.WaiterName);
        Assert.Equal(2, error.Attempts);
    }

    [Fact]
    public async Task TableExistsUsesTheModeledDelays()
    {
        var time = new InstantTimeProvider();
        var client = new ScriptedClient(
            Table(TableStatus.CREATING),
            Table(TableStatus.CREATING),
            Table(TableStatus.ACTIVE)
        );

        await client.WaitUntilTableExistsAsync(
            Input,
            new SmithyWaiterOptions(TimeSpan.FromMinutes(1)) { TimeProvider = time }
        );

        // minDelay 1, maxDelay 4: the first retry waits exactly the minimum.
        Assert.Equal(TimeSpan.FromSeconds(1), time.Delays[0]);
        Assert.InRange(time.Delays[1], TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task TableNotExistsSucceedsOnTheModeledError()
    {
        var client = new ScriptedClient(Table(TableStatus.ACTIVE), new ResourceNotFoundException());

        var result = await client.WaitUntilTableNotExistsAsync(Input, Options());

        Assert.IsType<ResourceNotFoundException>(result.Exception);
        Assert.Null(result.Output);
    }

    [Fact]
    public async Task AnUnmodeledErrorMatchesByItsErrorCode()
    {
        var client = new ScriptedClient(
            Table(TableStatus.ACTIVE),
            new SmithyClientException(HttpStatusCode.BadRequest, null, "Table.Gone")
        );

        var result = await client.WaitUntilTableGoneAsync(Input, Options());

        Assert.Equal(2, result.Attempts);
    }

    [Fact]
    public async Task AnUnmodeledErrorWithAnotherCodeFailsTheWaiter()
    {
        var other = new SmithyClientException(HttpStatusCode.BadRequest, null, "Throttled");
        var client = new ScriptedClient(other);

        var error = await Assert.ThrowsAsync<SmithyWaiterFailureException>(() =>
            client.WaitUntilTableGoneAsync(Input, Options())
        );

        Assert.Same(other, error.InnerException);
    }

    [Fact]
    public async Task AllStringEqualsNeedsANonEmptyListOfMatches()
    {
        var client = new ScriptedClient(
            Replicas(),
            Replicas(TableStatus.ACTIVE, TableStatus.CREATING),
            Replicas(TableStatus.ACTIVE, TableStatus.ACTIVE)
        );

        var result = await client.WaitUntilReplicasActiveAsync(Input, Options());

        Assert.Equal(3, result.Attempts);
    }

    [Fact]
    public async Task AnyStringEqualsMatchesASingleItem()
    {
        var client = new ScriptedClient(Replicas(TableStatus.ACTIVE, TableStatus.FAILED));

        await Assert.ThrowsAsync<SmithyWaiterFailureException>(() =>
            client.WaitUntilReplicasActiveAsync(Input, Options())
        );
    }

    [Fact]
    public async Task FiltersLengthsAndComparisons()
    {
        var client = new ScriptedClient(
            Replicas(),
            Replicas(TableStatus.ACTIVE, TableStatus.CREATING),
            Replicas(TableStatus.ACTIVE, TableStatus.ACTIVE)
        );

        var result = await client.WaitUntilReplicasFilledAsync(Input, Options());

        Assert.Equal(3, result.Attempts);
    }

    [Fact]
    public async Task FlattenedMultiSelectListsAndContains()
    {
        var client = new ScriptedClient(
            Sized((Size: 1, MinSize: 2), (Size: 3, MinSize: 2)),
            Sized((Size: 2, MinSize: 2), (Size: 3, MinSize: 2))
        );

        var result = await client.WaitUntilReplicasSizedAsync(Input, Options());

        Assert.Equal(2, result.Attempts);
    }

    [Fact]
    public async Task AndOfComparisons()
    {
        var client = new ScriptedClient(
            Table(TableStatus.CREATING),
            Table(TableStatus.UPDATING),
            Table(TableStatus.ACTIVE)
        );

        var result = await client.WaitUntilSettledAsync(Input, Options());

        Assert.Equal(3, result.Attempts);
    }

    [Fact]
    public async Task OrOfBooleans()
    {
        var client = new ScriptedClient(
            new DescribeTableOutput(new TableDescription(Archived: false, Deleted: false)),
            new DescribeTableOutput(new TableDescription(Deleted: true))
        );

        var result = await client.WaitUntilArchivedAsync(Input, Options());

        Assert.Equal(2, result.Attempts);
    }

    [Fact]
    public async Task ObjectProjectionsOverMapValues()
    {
        var client = new ScriptedClient(Settings(Mode.SLOW), Settings(Mode.SLOW, Mode.FAST));

        var result = await client.WaitUntilSettingsFastAsync(Input, Options());

        Assert.Equal(2, result.Attempts);
    }

    [Fact]
    public async Task AStringWithTheEnumTraitComparesAsAString()
    {
        var client = new ScriptedClient(
            new DescribeTableOutput(
                new TableDescription(
                    Settings: new Dictionary<string, Setting>
                    {
                        ["primary"] = new(Mode.SLOW, Tier: "GOLD"),
                    }
                )
            )
        );

        await Assert.ThrowsAsync<SmithyWaiterFailureException>(() =>
            client.WaitUntilSettingsFastAsync(Input, Options())
        );
    }

    [Fact]
    public async Task InputOutputPathsReadBoth()
    {
        var client = new ScriptedClient(
            new DescribeTableOutput(new TableDescription(TableName: "other")),
            new DescribeTableOutput(new TableDescription(TableName: "orders"))
        );

        var result = await client.WaitUntilNamedAsync(Input, Options());

        Assert.Equal(2, result.Attempts);
    }

    [Fact]
    public async Task TheMaxWaitTimeOverloadTimesOut()
    {
        var client = new ScriptedClient(
            Enumerable.Repeat<object>(Table(TableStatus.CREATING), 10).ToArray()
        );

        // TableExists waits at least a second between attempts, so a waiter given less than its
        // minimum delay stops after the first attempt.
        await Assert.ThrowsAsync<SmithyWaiterTimeoutException>(() =>
            client.WaitUntilTableExistsAsync(Input, TimeSpan.FromMilliseconds(500))
        );
        Assert.Equal(1, client.Attempts);
    }

    private static SmithyWaiterOptions Options() =>
        new(TimeSpan.FromMinutes(10)) { TimeProvider = new InstantTimeProvider() };

    private static DescribeTableOutput Table(TableStatus status) =>
        new(new TableDescription(TableName: "orders", TableStatus: status));

    private static DescribeTableOutput Replicas(params TableStatus[] statuses) =>
        new(
            new TableDescription(
                Replicas: [.. statuses.Select(status => new Replica(Status: status))]
            )
        );

    private static DescribeTableOutput Sized(params (int Size, int MinSize)[] replicas) =>
        new(
            new TableDescription(
                Replicas:
                [
                    .. replicas.Select(replica => new Replica(
                        Size: replica.Size,
                        MinSize: replica.MinSize
                    )),
                ]
            )
        );

    private static DescribeTableOutput Settings(params Mode[] modes) =>
        new(
            new TableDescription(
                Settings: modes
                    .Select((mode, index) => (mode, index))
                    .ToDictionary(item => $"setting{item.index}", item => new Setting(item.mode))
            )
        );

    /// <summary>Returns or throws one scripted item per call.</summary>
    private sealed class ScriptedClient(params object[] script) : FakeTablesClient
    {
        public int Attempts { get; private set; }

        public override Task<DescribeTableOutput> DescribeTableAsync(
            DescribeTableInput input,
            CancellationToken cancellationToken = default
        ) =>
            script[Attempts++] switch
            {
                Exception exception => Task.FromException<DescribeTableOutput>(exception),
                var output => Task.FromResult((DescribeTableOutput)output),
            };
    }

    /// <summary>A clock that jumps forward by each delay and fires its timer at once.</summary>
    private sealed class InstantTimeProvider : TimeProvider
    {
        private long ticks;

        public List<TimeSpan> Delays { get; } = [];

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
