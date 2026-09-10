namespace NSmithy.Messaging.Redis;

internal interface IRedisStreamBinding
{
    long? MaxLength { get; }
}

public sealed class RedisStreamSendBinding<T>(
    string serviceId,
    string operationId,
    string address,
    Func<T, MessagePayload> encode,
    long? maxLength = null
) : MessageSendBinding<T>(serviceId, operationId, address, encode), IRedisStreamBinding
{
    public long? MaxLength { get; } = maxLength;
}

public sealed class RedisStreamRequestBinding<TRequest, TReply>(
    string serviceId,
    string operationId,
    string address,
    Func<TRequest, MessagePayload> encode,
    Func<MessagePayload, TReply> decodeReply,
    long? maxLength = null
)
    : MessageRequestBinding<TRequest, TReply>(serviceId, operationId, address, encode, decodeReply),
        IRedisStreamBinding
{
    public long? MaxLength { get; } = maxLength;
}
