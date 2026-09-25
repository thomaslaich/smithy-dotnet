using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NSmithy.Messaging.Kafka;

/// <summary>
/// Sequential hosted consumer. Decode or processing failure stops the worker without advancing
/// the failed delivery. Restart replays from committed offsets; handlers must be idempotent.
/// </summary>
public sealed partial class KafkaMessageConsumer : BackgroundService
{
    private readonly IReadOnlyDictionary<string, MessageReceiveBinding> bindings;
    private readonly MessageProcessor processor;
    private readonly IServiceProvider services;
    private readonly Func<IKafkaConsumerDriver> createConsumer;
    private readonly ILogger<KafkaMessageConsumer> logger;

    public KafkaMessageConsumer(
        ConsumerConfig config,
        IEnumerable<MessageReceiveBinding> bindings,
        MessageProcessor processor,
        IServiceProvider services,
        ILogger<KafkaMessageConsumer> logger
    )
        : this(bindings, processor, services, CreateFactory(config), logger) { }

    internal KafkaMessageConsumer(
        IEnumerable<MessageReceiveBinding> bindings,
        MessageProcessor processor,
        IServiceProvider services,
        Func<IKafkaConsumerDriver> createConsumer,
        ILogger<KafkaMessageConsumer> logger
    )
    {
        ArgumentNullException.ThrowIfNull(bindings);
        this.bindings = bindings.ToDictionary(binding => binding.Address, StringComparer.Ordinal);
        if (this.bindings.Count == 0)
            throw new ArgumentException(
                "A consumer requires at least one operation binding.",
                nameof(bindings)
            );
        if (this.bindings.Values.Any(binding => binding.HasReply))
            throw new ArgumentException(
                "Kafka consumers do not support request/reply bindings.",
                nameof(bindings)
            );
        this.processor = processor;
        this.services = services;
        this.createConsumer = createConsumer;
        this.logger = logger;
    }

    private static Func<IKafkaConsumerDriver> CreateFactory(ConsumerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var copy = new ConsumerConfig(config);
        return () => new KafkaConsumerDriver(copy);
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var binding in bindings.Values)
            binding.Validate(services);
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var consumer = createConsumer();
        try
        {
            consumer.Subscribe(bindings.Keys);
            while (!stoppingToken.IsCancellationRequested)
            {
                var delivery = consumer.Consume(stoppingToken);
                if (delivery.IsPartitionEOF)
                    continue;
                var headers = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                if (delivery.Message.Headers is { } kafkaHeaders)
                    foreach (var header in kafkaHeaders)
                        headers[header.Key] = header.GetValueBytes();
                var payload = new MessagePayload(
                    delivery.Message.Value,
                    delivery.Message.Key,
                    headers
                );
                await processor
                    .ProcessAsync(bindings[delivery.Topic], payload, stoppingToken)
                    .ConfigureAwait(false);
                // Never advance past a failed handler or a cancelled delivery. Sequential processing
                // ensures the stored position describes a contiguous successful prefix per partition.
                stoppingToken.ThrowIfCancellationRequested();
                try
                {
                    consumer.StoreOffset(delivery);
                }
                catch (KafkaException exception)
                    when (exception.Error.Code == ErrorCode.Local_State)
                {
                    // Ownership was lost during processing. The new owner may replay this delivery.
                    LogOwnershipLost(logger, delivery.Topic, delivery.Partition.Value, exception);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            // Runs after the active handler finishes, on success, failure, and host cancellation.
            consumer.Close();
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Kafka partition ownership lost after processing {Topic}[{Partition}]; delivery may be replayed."
    )]
    private static partial void LogOwnershipLost(
        ILogger logger,
        string topic,
        int partition,
        Exception exception
    );
}
