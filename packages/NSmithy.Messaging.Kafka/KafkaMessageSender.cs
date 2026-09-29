using Confluent.Kafka;

namespace NSmithy.Messaging.Kafka;

/// <summary>Owns the producer; individual sends await Kafka's configured delivery confirmation.</summary>
public sealed class KafkaMessageSender : IMessageSender, IDisposable
{
    private readonly IProducer<string?, byte[]> producer;

    public KafkaMessageSender(ProducerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        producer = new ProducerBuilder<string?, byte[]>(new ProducerConfig(config)).Build();
    }

    public async Task SendAsync<T>(
        MessageSendBinding<T> binding,
        T message,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();
        var payload = binding.Encode(message);
        var headers = new Headers();
        if (payload.Headers is { } modeledHeaders)
            foreach (var header in modeledHeaders)
                headers.Add(header.Key, header.Value);
        await producer
            .ProduceAsync(
                binding.Address,
                new Message<string?, byte[]>
                {
                    Key = payload.Key,
                    Value = payload.Value,
                    Headers = headers,
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public void Dispose() => producer.Dispose();
}
