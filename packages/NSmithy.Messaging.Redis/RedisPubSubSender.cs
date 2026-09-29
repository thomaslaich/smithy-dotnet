using StackExchange.Redis;

namespace NSmithy.Messaging.Redis;

public sealed class RedisPubSubSender : IMessageSender
{
    private readonly IRedisDriver driver;

    public RedisPubSubSender(IConnectionMultiplexer connection)
        : this(new RedisDriver(connection, -1)) { }

    internal RedisPubSubSender(IRedisDriver driver) => this.driver = driver;

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
        await driver
            .PublishAsync(binding.Address, payload.Value)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
