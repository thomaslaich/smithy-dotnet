using StackExchange.Redis;

namespace NSmithy.Messaging.Redis;

internal sealed record RedisDelivery(
    string Id,
    byte[] Payload,
    string? ReplyTo = null,
    string? CorrelationId = null
);

internal sealed record RedisClaimPage(string NextPosition, IReadOnlyList<RedisDelivery> Deliveries);

// Redis-specific state remains behind this seam; it is not a general broker API.
internal interface IRedisDriver
{
    Task CreateGroupAsync(string address, string group, string position);
    Task<IReadOnlyList<RedisDelivery>> ReadGroupAsync(
        string address,
        string group,
        string consumer,
        int count
    );
    Task<RedisClaimPage> ClaimAsync(
        string address,
        string group,
        string consumer,
        long idleMilliseconds,
        string position,
        int count
    );
    Task<IReadOnlyList<RedisDelivery>> ReadAsync(string address, string position, int count);
    Task<string> LatestPositionAsync(string address);
    Task AcknowledgeAsync(string address, string group, string id);
    Task AppendAsync(
        string address,
        byte[] payload,
        long? maxLength,
        string? replyTo = null,
        string? correlationId = null
    );
    Task PublishAsync(string address, byte[] payload);
    Task<IAsyncDisposable> SubscribeAsync(string address, Action<byte[]> receive);
}

internal sealed class RedisDriver(IConnectionMultiplexer connection, int database) : IRedisDriver
{
    private readonly IDatabase database = connection.GetDatabase(database);
    private readonly ISubscriber subscriber = connection.GetSubscriber();

    public async Task CreateGroupAsync(string address, string group, string position)
    {
        try
        {
            await database
                .StreamCreateConsumerGroupAsync(address, group, position, createStream: true)
                .ConfigureAwait(false);
        }
        catch (RedisServerException exception)
            when (exception.Message.StartsWith("BUSYGROUP", StringComparison.Ordinal)) { }
    }

    public async Task<IReadOnlyList<RedisDelivery>> ReadGroupAsync(
        string address,
        string group,
        string consumer,
        int count
    ) =>
        Convert(
            await database
                .StreamReadGroupAsync(address, group, consumer, ">", count: count)
                .ConfigureAwait(false)
        );

    public async Task<RedisClaimPage> ClaimAsync(
        string address,
        string group,
        string consumer,
        long idleMilliseconds,
        string position,
        int count
    )
    {
        var result = await database
            .StreamAutoClaimAsync(address, group, consumer, idleMilliseconds, position, count)
            .ConfigureAwait(false);
        return new RedisClaimPage(result.NextStartId.ToString(), Convert(result.ClaimedEntries));
    }

    public async Task<IReadOnlyList<RedisDelivery>> ReadAsync(
        string address,
        string position,
        int count
    ) => Convert(await database.StreamReadAsync(address, position, count).ConfigureAwait(false));

    public async Task<string> LatestPositionAsync(string address)
    {
        var entries = await database
            .StreamRangeAsync(address, "-", "+", 1, Order.Descending)
            .ConfigureAwait(false);
        return entries.Length == 0 ? "0-0" : entries[0].Id.ToString();
    }

    public async Task AcknowledgeAsync(string address, string group, string id) =>
        _ = await database.StreamAcknowledgeAsync(address, group, id).ConfigureAwait(false);

    public async Task AppendAsync(
        string address,
        byte[] payload,
        long? maxLength,
        string? replyTo = null,
        string? correlationId = null
    )
    {
        NameValueEntry[] entries = replyTo is null
            ? [new("data", payload)]
            :
            [
                new("data", payload),
                new("reply_to", replyTo),
                new("correlation_id", correlationId),
            ];
        _ = await database
            .StreamAddAsync(
                address,
                entries,
                maxLength: maxLength is null ? null : checked((int)maxLength.Value),
                useApproximateMaxLength: true
            )
            .ConfigureAwait(false);
    }

    public async Task PublishAsync(string address, byte[] payload) =>
        _ = await subscriber
            .PublishAsync(RedisChannel.Literal(address), payload)
            .ConfigureAwait(false);

    public async Task<IAsyncDisposable> SubscribeAsync(string address, Action<byte[]> receive)
    {
        var queue = await subscriber
            .SubscribeAsync(RedisChannel.Literal(address))
            .ConfigureAwait(false);
        queue.OnMessage(message => receive((byte[]?)message.Message ?? []));
        return new Subscription(queue);
    }

    private sealed class Subscription(ChannelMessageQueue queue) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() =>
            await queue.UnsubscribeAsync().ConfigureAwait(false);
    }

    private static RedisDelivery[] Convert(StreamEntry[] entries) =>
        entries
            .Select(entry =>
            {
                byte[]? payload = null;
                string? replyTo = null,
                    correlationId = null;
                foreach (var field in entry.Values)
                {
                    if (field.Name == "data")
                        payload = (byte[]?)field.Value;
                    else if (field.Name == "reply_to")
                        replyTo = (string?)field.Value;
                    else if (field.Name == "correlation_id")
                        correlationId = (string?)field.Value;
                }
                return new RedisDelivery(
                    entry.Id.ToString(),
                    payload
                        ?? throw new InvalidOperationException(
                            "Redis stream entry has no data field."
                        ),
                    replyTo,
                    correlationId
                );
            })
            .ToArray();
}
