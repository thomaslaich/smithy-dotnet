using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace NSmithy.Messaging.Redis;

/// <summary>Runtime-owned group recovery, processing, reply publication, and settlement.</summary>
public sealed class RedisStreamsConsumer : BackgroundService
{
    private readonly IRedisDriver driver;
    private readonly MessageReceiveBinding[] bindings;
    private readonly MessageProcessor processor;
    private readonly IServiceProvider services;
    private readonly RedisStreamConsumerOptions options;
    private readonly TimeProvider timeProvider;
    private readonly Dictionary<string, string> positions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> nextClaims = new(StringComparer.Ordinal);

    public RedisStreamsConsumer(
        IConnectionMultiplexer connection,
        IEnumerable<MessageReceiveBinding> bindings,
        MessageProcessor processor,
        IServiceProvider services,
        RedisStreamConsumerOptions? options = null,
        int database = -1
    )
        : this(
            new RedisDriver(connection, database),
            bindings,
            processor,
            services,
            options ?? new(),
            TimeProvider.System
        ) { }

    internal RedisStreamsConsumer(
        IRedisDriver driver,
        IEnumerable<MessageReceiveBinding> bindings,
        MessageProcessor processor,
        IServiceProvider services,
        RedisStreamConsumerOptions options,
        TimeProvider? timeProvider = null
    )
    {
        ArgumentNullException.ThrowIfNull(bindings);
        this.bindings = bindings.ToArray();
        if (
            this.bindings.Length == 0
            || this.bindings.Select(b => b.Address).Distinct(StringComparer.Ordinal).Count()
                != this.bindings.Length
        )
            throw new ArgumentException(
                "A consumer requires unique operation addresses.",
                nameof(bindings)
            );
        options.Validate();
        if (
            options.ReadMode == RedisStreamReadMode.Independent
            && this.bindings.Any(b => b.HasReply)
        )
            throw new ArgumentException(
                "Request/reply operations require consumer-group processing.",
                nameof(options)
            );
        this.driver = driver;
        this.processor = processor;
        this.services = services;
        this.options = options;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var binding in bindings)
            binding.Validate(services);
        foreach (var binding in bindings)
        {
            if (options.ReadMode == RedisStreamReadMode.ConsumerGroup)
            {
                await driver
                    .CreateGroupAsync(binding.Address, options.ConsumerGroup, options.StartPosition)
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                positions[binding.Address] = "0-0";
                nextClaims[binding.Address] = DateTimeOffset.MinValue;
            }
            else
            {
                var saved = options.CheckpointStore is { } store
                    ? await store
                        .LoadAsync(options.CheckpointName, binding.Address, cancellationToken)
                        .ConfigureAwait(false)
                    : null;
                var position = saved ?? options.StartPosition;
                positions[binding.Address] =
                    position == "$"
                        ? await driver
                            .LatestPositionAsync(binding.Address)
                            .WaitAsync(cancellationToken)
                            .ConfigureAwait(false)
                        : position;
            }
        }
        // Resolve "$" before later hosted publishers start; otherwise their first events can be skipped.
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (options.ReadMode == RedisStreamReadMode.Independent)
                await ReadIndependentlyAsync(stoppingToken).ConfigureAwait(false);
            else
                await ReadGroupAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task ReadGroupAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var received = false;
            foreach (var binding in bindings)
            {
                var address = binding.Address;
                if (positions[address] != "0-0" || timeProvider.GetUtcNow() >= nextClaims[address])
                {
                    var page = await driver
                        .ClaimAsync(
                            address,
                            options.ConsumerGroup,
                            options.ConsumerName,
                            checked((long)options.MinIdleTime.TotalMilliseconds),
                            positions[address],
                            options.ReadCount
                        )
                        .WaitAsync(ct)
                        .ConfigureAwait(false);
                    foreach (var delivery in page.Deliveries)
                    {
                        await ProcessGroupDeliveryAsync(binding, delivery, ct)
                            .ConfigureAwait(false);
                        received = true;
                    }
                    positions[address] = page.NextPosition;
                    if (page.NextPosition == "0-0")
                        nextClaims[address] = timeProvider.GetUtcNow() + options.ClaimInterval;
                    // An empty page can still have a nonzero scan cursor. Continue recovery promptly.
                    else
                        received = true;
                }
                var entries = await driver
                    .ReadGroupAsync(
                        address,
                        options.ConsumerGroup,
                        options.ConsumerName,
                        options.ReadCount
                    )
                    .WaitAsync(ct)
                    .ConfigureAwait(false);
                foreach (var delivery in entries)
                {
                    await ProcessGroupDeliveryAsync(binding, delivery, ct).ConfigureAwait(false);
                    received = true;
                }
            }
            if (!received)
                await Task.Delay(options.PollInterval, timeProvider, ct).ConfigureAwait(false);
        }
    }

    private async Task ProcessGroupDeliveryAsync(
        MessageReceiveBinding binding,
        RedisDelivery delivery,
        CancellationToken ct
    )
    {
        if (
            binding.HasReply
            && (
                string.IsNullOrEmpty(delivery.ReplyTo)
                || string.IsNullOrEmpty(delivery.CorrelationId)
            )
        )
            throw new InvalidOperationException(
                $"Request for {binding.OperationId} has no reply destination or correlation ID."
            );
        var reply = await processor
            .ProcessAsync(binding, new MessagePayload(delivery.Payload), ct)
            .ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (reply is not null)
        {
            await driver
                .PublishAsync(
                    delivery.ReplyTo!,
                    RedisReplyEnvelope.Encode(delivery.CorrelationId!, reply.Value)
                )
                .WaitAsync(ct)
                .ConfigureAwait(false);
        }
        ct.ThrowIfCancellationRequested();
        await driver
            .AcknowledgeAsync(binding.Address, options.ConsumerGroup, delivery.Id)
            .WaitAsync(ct)
            .ConfigureAwait(false);
    }

    private async Task ReadIndependentlyAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var received = false;
            foreach (var binding in bindings)
            {
                var entries = await driver
                    .ReadAsync(binding.Address, positions[binding.Address], options.ReadCount)
                    .WaitAsync(ct)
                    .ConfigureAwait(false);
                foreach (var delivery in entries)
                {
                    await processor
                        .ProcessAsync(binding, new MessagePayload(delivery.Payload), ct)
                        .ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    if (options.CheckpointStore is { } store)
                        await store
                            .SaveAsync(options.CheckpointName, binding.Address, delivery.Id, ct)
                            .ConfigureAwait(false);
                    positions[binding.Address] = delivery.Id;
                    received = true;
                }
            }
            if (!received)
                await Task.Delay(options.PollInterval, timeProvider, ct).ConfigureAwait(false);
        }
    }
}
