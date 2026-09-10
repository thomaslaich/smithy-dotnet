namespace NSmithy.Messaging.Redis;

public sealed record RedisMessagingOptions
{
    public int Database { get; init; } = -1;
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

public enum RedisStreamReadMode
{
    ConsumerGroup,
    Independent,
}

public sealed record RedisStreamConsumerOptions
{
    public RedisStreamReadMode ReadMode { get; init; }
    public string ConsumerGroup { get; init; } = "nsmithy";
    public string ConsumerName { get; init; } = Guid.NewGuid().ToString("N");
    public string StartPosition { get; init; } = "0-0";
    public int ReadCount { get; init; } = 10;
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(100);
    public TimeSpan MinIdleTime { get; init; } = TimeSpan.FromMinutes(1);
    public TimeSpan ClaimInterval { get; init; } = TimeSpan.FromSeconds(5);
    public IRedisStreamCheckpointStore? CheckpointStore { get; init; }
    public string CheckpointName { get; init; } = "nsmithy";

    internal void Validate()
    {
        if (!Enum.IsDefined(ReadMode))
            throw new ArgumentOutOfRangeException(nameof(ReadMode));
        ArgumentException.ThrowIfNullOrWhiteSpace(ConsumerGroup);
        ArgumentException.ThrowIfNullOrWhiteSpace(ConsumerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(StartPosition);
        ArgumentException.ThrowIfNullOrWhiteSpace(CheckpointName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ReadCount);
        if (
            PollInterval <= TimeSpan.Zero
            || ClaimInterval <= TimeSpan.Zero
            || MinIdleTime < TimeSpan.Zero
        )
            throw new ArgumentException(
                "Polling and claim intervals must be positive; minimum idle time must be nonnegative."
            );
    }
}

public sealed record RedisPubSubConsumerOptions
{
    public int Capacity { get; init; } = 1024;
}

/// <summary>Composition-root persistence for independent XREAD readers, never passed to handlers.</summary>
public interface IRedisStreamCheckpointStore
{
    Task<string?> LoadAsync(string reader, string address, CancellationToken cancellationToken);
    Task SaveAsync(
        string reader,
        string address,
        string position,
        CancellationToken cancellationToken
    );
}
