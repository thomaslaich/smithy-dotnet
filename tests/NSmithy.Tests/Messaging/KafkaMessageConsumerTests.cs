using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSmithy.Messaging;
using NSmithy.Messaging.Kafka;

namespace NSmithy.Tests.Messaging;

public sealed class KafkaMessageConsumerTests
{
    [Fact]
    public async Task SettlesOnlySuccessfulPrefixAndClosesAfterFailure()
    {
        var handled = new List<int>();
        var disposed = new List<int>();
        var driver = new FakeConsumer([Delivery(0), Delivery(1), Delivery(2)]);
        var services = new ServiceCollection();
        services.AddScoped(_ => new Handler(handled, disposed, failAt: 1));
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true }
        );
        using var consumer = CreateConsumer(provider, driver);

        await consumer.StartAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            consumer.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5))
        );

        Assert.Equal([0, 1], handled);
        Assert.Equal([0, 1], disposed);
        Assert.Equal([0L], driver.Stored);
        Assert.True(driver.Closed);
        Assert.True(driver.Disposed);
    }

    [Fact]
    public async Task LostOwnershipDoesNotSettleOrPreventTheNextDelivery()
    {
        var handled = new List<int>();
        var driver = new FakeConsumer([Delivery(0), Delivery(1)]) { LoseOwnershipAt = 0 };
        var services = new ServiceCollection().AddScoped(_ => new Handler(handled, []));
        await using var provider = services.BuildServiceProvider();
        using var consumer = CreateConsumer(provider, driver);

        await consumer.StartAsync(CancellationToken.None);
        await driver.Drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await consumer.StopAsync(CancellationToken.None);

        Assert.Equal([0, 1], handled);
        Assert.Equal([1L], driver.Stored);
        Assert.True(driver.Closed);
        Assert.True(driver.Disposed);
    }

    [Fact]
    public async Task ShutdownWaitsForHandlerAndDoesNotSettleCancelledDelivery()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var driver = new FakeConsumer([Delivery(0)]);
        var services = new ServiceCollection().AddScoped(_ => new Handler(
            [],
            [],
            started: started,
            release: release
        ));
        await using var provider = services.BuildServiceProvider();
        using var consumer = CreateConsumer(provider, driver);
        await consumer.StartAsync(CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var stopping = consumer.StopAsync(CancellationToken.None);
        Assert.False(driver.Closed);
        Assert.False(stopping.IsCompleted);
        release.SetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(driver.Stored);
        Assert.True(driver.Closed);
        Assert.True(driver.Disposed);
    }

    [Fact]
    public async Task MissingHandlerFailsStartupBeforeConnectingToKafka()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var driver = new FakeConsumer([]);
        using var consumer = CreateConsumer(provider, driver);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            consumer.StartAsync(CancellationToken.None)
        );
        Assert.False(driver.Subscribed);
    }

    [Fact]
    public async Task DecodeFailureDoesNotSettleOrConstructHandler()
    {
        var constructed = false;
        var services = new ServiceCollection().AddScoped(_ =>
        {
            constructed = true;
            return new Handler([], []);
        });
        await using var provider = services.BuildServiceProvider();
        var driver = new FakeConsumer([Delivery(0)]);
        var binding = new MessageReceiveBinding<int, Handler>(
            "test#Service",
            "test#Read",
            "test",
            _ => throw new FormatException("invalid message"),
            (handler, value, ct) => handler.HandleAsync(value, ct)
        );
        using var consumer = CreateConsumer(provider, driver, binding);
        await consumer.StartAsync(CancellationToken.None);
        await Assert.ThrowsAsync<FormatException>(() =>
            consumer.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5))
        );
        Assert.False(constructed);
        Assert.Empty(driver.Stored);
        Assert.True(driver.Closed);
    }

    private static KafkaMessageConsumer CreateConsumer(
        ServiceProvider provider,
        FakeConsumer driver,
        MessageReceiveBinding? binding = null
    )
    {
        binding ??= new MessageReceiveBinding<int, Handler>(
            "test#Service",
            "test#Read",
            "test",
            payload => payload.Value[0],
            (handler, value, ct) => handler.HandleAsync(value, ct)
        );
        return new KafkaMessageConsumer(
            [binding],
            new MessageProcessor(provider.GetRequiredService<IServiceScopeFactory>()),
            provider,
            () => driver,
            NullLogger<KafkaMessageConsumer>.Instance
        );
    }

    private static ConsumeResult<string?, byte[]> Delivery(byte value) =>
        new()
        {
            Topic = "test",
            Partition = 0,
            Offset = value,
            Message = new Message<string?, byte[]> { Value = [value] },
        };

    private sealed class Handler(
        List<int> handled,
        List<int> disposed,
        int failAt = -1,
        TaskCompletionSource? started = null,
        TaskCompletionSource? release = null
    ) : IAsyncDisposable
    {
        private int value;

        public async Task HandleAsync(int message, CancellationToken cancellationToken)
        {
            value = message;
            handled.Add(message);
            started?.SetResult();
            if (release is not null)
                await release.Task;
            cancellationToken.ThrowIfCancellationRequested();
            if (message == failAt)
                throw new InvalidOperationException("handler failed");
        }

        public ValueTask DisposeAsync()
        {
            disposed.Add(value);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeConsumer(IEnumerable<ConsumeResult<string?, byte[]>> deliveries)
        : IKafkaConsumerDriver
    {
        private readonly Queue<ConsumeResult<string?, byte[]>> deliveries = new(deliveries);
        public List<long> Stored { get; } = [];
        public TaskCompletionSource Drained { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public long LoseOwnershipAt { get; init; } = -1;
        public bool Subscribed { get; private set; }
        public bool Closed { get; private set; }
        public bool Disposed { get; private set; }

        public void Subscribe(IEnumerable<string> topics) => Subscribed = true;

        public ConsumeResult<string?, byte[]> Consume(CancellationToken cancellationToken)
        {
            if (deliveries.TryDequeue(out var delivery))
                return delivery;
            Drained.TrySetResult();
            cancellationToken.WaitHandle.WaitOne();
            throw new OperationCanceledException(cancellationToken);
        }

        public void StoreOffset(ConsumeResult<string?, byte[]> delivery)
        {
            if (delivery.Offset.Value == LoseOwnershipAt)
                throw new KafkaException(new Error(ErrorCode.Local_State));
            Stored.Add(delivery.Offset.Value);
        }

        public void Close() => Closed = true;

        public void Dispose() => Disposed = true;
    }
}
