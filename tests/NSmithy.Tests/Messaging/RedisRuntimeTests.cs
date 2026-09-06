using Microsoft.Extensions.DependencyInjection;
using NSmithy.Messaging;
using NSmithy.Messaging.Redis;

namespace NSmithy.Tests.Messaging;

public sealed class RedisRuntimeTests
{
    [Fact]
    public async Task RecoveryFollowsEmptyClaimPagesAndAcknowledgesAfterHandlerDisposal()
    {
        var driver = new FakeRedis();
        driver.Claim = position =>
            position == "0-0" ? new("12-0", []) : new("0-0", [new("12-0", [12])]);
        await using var services = Services(
            (value, _) =>
            {
                driver.Calls.Add($"handle:{value}");
                return Task.CompletedTask;
            },
            () => driver.Calls.Add("dispose")
        );
        using var consumer = Consumer(driver, services);
        await consumer.StartAsync(CancellationToken.None);
        await driver.Acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await consumer.StopAsync(CancellationToken.None);
        Assert.Contains("claim:0-0", driver.Calls);
        Assert.Contains("claim:12-0", driver.Calls);
        Assert.True(driver.Calls.IndexOf("handle:12") < driver.Calls.IndexOf("dispose"));
        Assert.True(driver.Calls.IndexOf("dispose") < driver.Calls.IndexOf("ack:12-0"));
    }

    [Fact]
    public async Task HandlerFailureLeavesPendingDeliveryUnacknowledged()
    {
        var driver = new FakeRedis { Claim = _ => new("0-0", [new("1-0", [1]), new("2-0", [2])]) };
        await using var services = Services(
            (_, _) => throw new InvalidOperationException("failed")
        );
        using var consumer = Consumer(driver, services);
        await consumer.StartAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            consumer.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5))
        );
        Assert.DoesNotContain(
            driver.Calls,
            call => call.StartsWith("ack:", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task ReplyPublicationFailureDoesNotAcknowledgeRequest()
    {
        var driver = new FakeRedis
        {
            Claim = _ => new("0-0", [new("1-0", [1], "reply", "correlation")]),
            Publish = (_, _) => throw new IOException("disconnected"),
        };
        await using var services = new ServiceCollection()
            .AddScoped<IReplyHandler, ReplyHandler>()
            .BuildServiceProvider();
        var binding = new MessageReplyReceiveBinding<int, int, IReplyHandler>(
            "test#Service",
            "test#Read",
            "test",
            payload => payload.Value[0],
            reply => new MessagePayload([(byte)('0' + reply)]),
            (handler, value, ct) => handler.HandleAsync(value, ct)
        );
        using var consumer = new RedisStreamsConsumer(
            driver,
            [binding],
            Processor(services),
            services,
            new()
        );
        await consumer.StartAsync(CancellationToken.None);
        await Assert.ThrowsAsync<IOException>(() =>
            consumer.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5))
        );
        Assert.DoesNotContain(
            driver.Calls,
            call => call.StartsWith("ack:", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task IndependentReaderLoadsCheckpointAndSavesOnlySuccessfulMessages()
    {
        var store = new Checkpoints();
        var driver = new FakeRedis { Read = _ => [new("6-0", [6]), new("7-0", [7])] };
        await using var services = Services(
            (value, _) => value == 7 ? throw new IOException("failed") : Task.CompletedTask
        );
        using var consumer = Consumer(
            driver,
            services,
            new() { ReadMode = RedisStreamReadMode.Independent, CheckpointStore = store }
        );
        await consumer.StartAsync(CancellationToken.None);
        await Assert.ThrowsAsync<IOException>(() =>
            consumer.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5))
        );
        Assert.Contains("read:5-0", driver.Calls);
        Assert.Equal(["6-0"], store.Saved);
        Assert.DoesNotContain(
            driver.Calls,
            call => call.StartsWith("ack:", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task RequestSubscribesBeforeSendingChecksCorrelationAndCleansUp()
    {
        var driver = new FakeRedis();
        driver.Append = (_, _, _, replyTo, correlationId) =>
        {
            driver.Fire(replyTo!, RedisReplyEnvelope.Encode("different", "0"u8.ToArray()));
            driver.Fire(replyTo!, RedisReplyEnvelope.Encode(correlationId!, "42"u8.ToArray()));
        };
        var sender = new RedisStreamsSender(driver, new());
        var reply = await sender.RequestAsync(RequestBinding(), 1);
        Assert.Equal(42, reply);
        Assert.Equal("subscribe", driver.Calls[0]);
        Assert.Equal("append", driver.Calls[1]);
        Assert.Equal("unsubscribe", driver.Calls[^1]);
        Assert.Empty(driver.Subscriptions);
    }

    [Fact]
    public async Task CancelledSubscriptionEstablishmentIsCleanedUpWithoutPublishing()
    {
        var driver = new FakeRedis
        {
            SubscribeGate = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var sender = new RedisStreamsSender(driver, new());
        using var cancellation = new CancellationTokenSource();
        var request = sender.RequestAsync(RequestBinding(), 1, cancellation.Token);
        await driver.Subscribed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        driver.SubscribeGate.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            request.WaitAsync(TimeSpan.FromSeconds(5))
        );
        Assert.DoesNotContain("append", driver.Calls);
        Assert.Empty(driver.Subscriptions);
    }

    [Fact]
    public async Task RequestTimeoutCleansUpSubscription()
    {
        var driver = new FakeRedis();
        var sender = new RedisStreamsSender(
            driver,
            new() { RequestTimeout = TimeSpan.FromMilliseconds(30) }
        );
        await Assert.ThrowsAsync<TimeoutException>(() => sender.RequestAsync(RequestBinding(), 1));
        Assert.Empty(driver.Subscriptions);
    }

    [Fact]
    public async Task PubSubOverflowStopsWorkerAndUnsubscribes()
    {
        var driver = new FakeRedis();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var services = Services(
            async (_, _) =>
            {
                started.SetResult();
                await release.Task;
            }
        );
        using var consumer = new RedisPubSubConsumer(
            driver,
            [Binding()],
            Processor(services),
            services,
            new() { Capacity = 1 }
        );
        await consumer.StartAsync(CancellationToken.None);
        await driver.Subscribed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        driver.Fire("test", [1]);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        driver.Fire("test", [2]);
        driver.Fire("test", [3]);
        release.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            consumer.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5))
        );
        Assert.Empty(driver.Subscriptions);
    }

    [Fact]
    public async Task PartialSubscriptionFailureCleansUpAlreadyEstablishedSubscriptions()
    {
        var driver = new FakeRedis { FailSubscription = "other" };
        await using var services = Services((_, _) => Task.CompletedTask);
        using var consumer = new RedisPubSubConsumer(
            driver,
            [Binding(), Binding("other")],
            Processor(services),
            services,
            new()
        );
        await Assert.ThrowsAsync<IOException>(() => consumer.StartAsync(CancellationToken.None));
        Assert.Empty(driver.Subscriptions);
    }

    private static MessageRequestBinding<int, int> RequestBinding() =>
        new(
            "test#Service",
            "test#Read",
            "test",
            _ => new MessagePayload("1"u8.ToArray()),
            payload =>
                int.Parse(
                    System.Text.Encoding.UTF8.GetString(payload.Value),
                    System.Globalization.CultureInfo.InvariantCulture
                )
        );

    private static MessageReceiveBinding<int, Handler> Binding(string address = "test") =>
        new MessageReceiveBinding<int, Handler>(
            "test#Service",
            "test#Read",
            address,
            payload => payload.Value[0],
            (handler, value, ct) => handler.Handle(value, ct)
        );

    private static MessageProcessor Processor(ServiceProvider services) =>
        new(services.GetRequiredService<IServiceScopeFactory>());

    private static RedisStreamsConsumer Consumer(
        FakeRedis driver,
        ServiceProvider services,
        RedisStreamConsumerOptions? options = null
    ) => new(driver, [Binding()], Processor(services), services, options ?? new());

    private static ServiceProvider Services(
        Func<int, CancellationToken, Task> handle,
        Action? dispose = null
    ) =>
        new ServiceCollection()
            .AddScoped(_ => new Handler(handle, dispose))
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

    private sealed class Handler(Func<int, CancellationToken, Task> handle, Action? dispose)
        : IAsyncDisposable
    {
        public Task Handle(int value, CancellationToken ct) => handle(value, ct);

        public ValueTask DisposeAsync()
        {
            dispose?.Invoke();
            return ValueTask.CompletedTask;
        }
    }

    private interface IReplyHandler
    {
        Task<int> HandleAsync(int value, CancellationToken ct);
    }

    private sealed class ReplyHandler : IReplyHandler
    {
        public Task<int> HandleAsync(int value, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(value);
        }
    }

    private sealed class Checkpoints : IRedisStreamCheckpointStore
    {
        public List<string> Saved { get; } = [];

        public Task<string?> LoadAsync(
            string reader,
            string address,
            CancellationToken cancellationToken
        ) => Task.FromResult<string?>("5-0");

        public Task SaveAsync(
            string reader,
            string address,
            string position,
            CancellationToken cancellationToken
        )
        {
            Saved.Add(position);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRedis : IRedisDriver
    {
        public List<string> Calls { get; } = [];
        public Dictionary<string, Action<byte[]>> Subscriptions { get; } = [];
        public TaskCompletionSource Subscribed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Acknowledged { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? SubscribeGate { get; init; }
        public string? FailSubscription { get; init; }
        public Func<string, RedisClaimPage> Claim { get; set; } = _ => new("0-0", []);
        public Func<string, IReadOnlyList<RedisDelivery>> Read { get; init; } = _ => [];
        public Action<string, byte[]> Publish { get; init; } = (_, _) => { };
        public Action<string, byte[], long?, string?, string?> Append { get; set; } =
            (_, _, _, _, _) => { };

        public Task CreateGroupAsync(string address, string group, string position) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<RedisDelivery>> ReadGroupAsync(
            string address,
            string group,
            string consumer,
            int count
        ) => Task.FromResult<IReadOnlyList<RedisDelivery>>([]);

        public Task<RedisClaimPage> ClaimAsync(
            string address,
            string group,
            string consumer,
            long idleMilliseconds,
            string position,
            int count
        )
        {
            Calls.Add("claim:" + position);
            return Task.FromResult(Claim(position));
        }

        public Task<IReadOnlyList<RedisDelivery>> ReadAsync(
            string address,
            string position,
            int count
        )
        {
            Calls.Add("read:" + position);
            return Task.FromResult(Read(position));
        }

        public Task<string> LatestPositionAsync(string address) => Task.FromResult("0-0");

        public Task AcknowledgeAsync(string address, string group, string id)
        {
            Calls.Add("ack:" + id);
            Acknowledged.TrySetResult();
            return Task.CompletedTask;
        }

        public Task AppendAsync(
            string address,
            byte[] payload,
            long? maxLength,
            string? replyTo = null,
            string? correlationId = null
        )
        {
            Calls.Add("append");
            Append(address, payload, maxLength, replyTo, correlationId);
            return Task.CompletedTask;
        }

        public Task PublishAsync(string address, byte[] payload)
        {
            Calls.Add("publish");
            Publish(address, payload);
            return Task.CompletedTask;
        }

        public async Task<IAsyncDisposable> SubscribeAsync(string address, Action<byte[]> receive)
        {
            if (address == FailSubscription)
                throw new IOException("subscribe failed");
            Calls.Add("subscribe");
            Subscriptions.Add(address, receive);
            Subscribed.TrySetResult();
            if (SubscribeGate is not null)
                await SubscribeGate.Task;
            return new Subscription(() =>
            {
                Calls.Add("unsubscribe");
                Subscriptions.Remove(address);
            });
        }

        public void Fire(string address, byte[] payload) => Subscriptions[address](payload);

        private sealed class Subscription(Action dispose) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
