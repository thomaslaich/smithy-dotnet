using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace NSmithy.Messaging.Redis;

/// <summary>At-most-once handler dispatch. Processing failure or buffer overflow stops the worker.</summary>
public sealed class RedisPubSubConsumer : BackgroundService
{
    private readonly IRedisDriver driver;
    private readonly MessageReceiveBinding[] bindings;
    private readonly MessageProcessor processor;
    private readonly IServiceProvider services;
    private readonly Channel<(MessageReceiveBinding Binding, byte[] Payload)> queue;
    private readonly List<IAsyncDisposable> subscriptions = [];
    private Exception? overflow;

    public RedisPubSubConsumer(
        IConnectionMultiplexer connection,
        IEnumerable<MessageReceiveBinding> bindings,
        MessageProcessor processor,
        IServiceProvider services,
        RedisPubSubConsumerOptions? options = null
    )
        : this(new RedisDriver(connection, -1), bindings, processor, services, options ?? new()) { }

    internal RedisPubSubConsumer(
        IRedisDriver driver,
        IEnumerable<MessageReceiveBinding> bindings,
        MessageProcessor processor,
        IServiceProvider services,
        RedisPubSubConsumerOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(bindings);
        this.bindings = bindings.ToArray();
        if (
            this.bindings.Length == 0
            || this.bindings.Any(b => b.HasReply)
            || this.bindings.Select(b => b.Address).Distinct(StringComparer.Ordinal).Count()
                != this.bindings.Length
        )
            throw new ArgumentException(
                "Pub/Sub requires unique, one-way operation bindings.",
                nameof(bindings)
            );
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Capacity);
        this.driver = driver;
        this.processor = processor;
        this.services = services;
        queue = Channel.CreateBounded<(MessageReceiveBinding Binding, byte[] Payload)>(
            new BoundedChannelOptions(options.Capacity)
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.Wait,
            }
        );
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var binding in bindings)
            binding.Validate(services);
        try
        {
            foreach (var binding in bindings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                subscriptions.Add(
                    await driver
                        .SubscribeAsync(
                            binding.Address,
                            payload =>
                            {
                                if (!queue.Writer.TryWrite((binding, payload)))
                                {
                                    Interlocked.CompareExchange(
                                        ref overflow,
                                        new InvalidOperationException(
                                            "Redis Pub/Sub buffer capacity exceeded; messages cannot be replayed."
                                        ),
                                        null
                                    );
                                    queue.Writer.TryComplete(overflow);
                                }
                            }
                        )
                        .ConfigureAwait(false)
                );
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref overflow) is { } failure)
                throw failure;
            await base.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await UnsubscribeAsync().ConfigureAwait(false);
            throw;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (
                var message in queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false)
            )
            {
                if (Volatile.Read(ref overflow) is { } failure)
                    throw failure;
                await processor
                    .ProcessAsync(
                        message.Binding,
                        new MessagePayload(message.Payload),
                        stoppingToken
                    )
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            await UnsubscribeAsync().ConfigureAwait(false);
        }
    }

    private async Task UnsubscribeAsync()
    {
        // Attempt every unsubscribe even if one fails.
        await Task.WhenAll(
                subscriptions.Select(subscription => subscription.DisposeAsync().AsTask())
            )
            .ConfigureAwait(false);
        queue.Writer.TryComplete();
    }
}
