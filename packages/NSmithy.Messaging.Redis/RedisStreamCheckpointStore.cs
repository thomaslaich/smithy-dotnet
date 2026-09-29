using StackExchange.Redis;

namespace NSmithy.Messaging.Redis;

/// <summary>
/// Stores XREAD progress in Redis. Use a stable reader name and one active reader per checkpoint.
/// Checkpoint durability follows the Redis deployment's persistence settings.
/// </summary>
public sealed class RedisStreamCheckpointStore(
    IConnectionMultiplexer connection,
    string keyPrefix = "nsmithy:checkpoint:",
    int database = -1
) : IRedisStreamCheckpointStore
{
    private readonly IDatabase database = connection.GetDatabase(database);

    public async Task<string?> LoadAsync(
        string reader,
        string address,
        CancellationToken cancellationToken
    ) =>
        (string?)
            await database
                .StringGetAsync(Key(reader, address))
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

    public async Task SaveAsync(
        string reader,
        string address,
        string position,
        CancellationToken cancellationToken
    ) =>
        _ = await database
            .StringSetAsync(Key(reader, address), position)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

    private string Key(string reader, string address) =>
        keyPrefix + Uri.EscapeDataString(reader) + ":" + Uri.EscapeDataString(address);
}
