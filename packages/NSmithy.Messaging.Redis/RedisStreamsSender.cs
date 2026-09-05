using StackExchange.Redis;

namespace NSmithy.Messaging.Redis;

public sealed class RedisStreamsSender : IMessageSender, IMessageRequestSender
{
    private readonly IRedisDriver driver;
    private readonly RedisMessagingOptions options;

    public RedisStreamsSender(
        IConnectionMultiplexer connection,
        RedisMessagingOptions? options = null
    )
        : this(new RedisDriver(connection, (options ?? new()).Database), options ?? new()) { }

    internal RedisStreamsSender(IRedisDriver driver, RedisMessagingOptions options)
    {
        this.driver = driver;
        this.options = options;
        if (options.RequestTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options));
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
        await driver
            .AppendAsync(
                binding.Address,
                payload.Value,
                (binding as IRedisStreamBinding)?.MaxLength
            )
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<TReply> RequestAsync<TRequest, TReply>(
        MessageRequestBinding<TRequest, TReply> binding,
        TRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var payload = binding.Encode(request);
        var correlationId = Guid.NewGuid().ToString("N");
        var replyTo = "bote:reply:" + correlationId;
        var completion = new TaskCompletionSource<byte[]>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.RequestTimeout);
        // Subscription establishment is awaited to completion so even cancellation cannot orphan
        // a late subscription. Redis's command timeout bounds that wait; disposal is unconditional.
        await using var subscription = await driver
            .SubscribeAsync(
                replyTo,
                bytes =>
                {
                    try
                    {
                        var reply = RedisReplyEnvelope.Decode(bytes);
                        if (reply.CorrelationId == correlationId)
                            completion.TrySetResult(reply.Payload);
                    }
                    catch (Exception exception)
                    {
                        completion.TrySetException(exception);
                    }
                }
            )
            .ConfigureAwait(false);
        try
        {
            deadline.Token.ThrowIfCancellationRequested();
            await driver
                .AppendAsync(
                    binding.Address,
                    payload.Value,
                    (binding as IRedisStreamBinding)?.MaxLength,
                    replyTo,
                    correlationId
                )
                .WaitAsync(deadline.Token)
                .ConfigureAwait(false);
            var reply = await completion.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            return binding.DecodeReply(new MessagePayload(reply));
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Reply timed out for operation {binding.OperationId}.",
                exception
            );
        }
    }
}
