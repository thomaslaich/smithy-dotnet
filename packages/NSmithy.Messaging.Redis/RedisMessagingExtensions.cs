using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace NSmithy.Messaging.Redis;

public static class RedisMessagingExtensions
{
    /// <summary>The caller owns the supplied connection and disposes it after the host stops.</summary>
    public static IServiceCollection AddRedisStreamsMessaging(
        this IServiceCollection services,
        IConnectionMultiplexer connection,
        RedisMessagingOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connection);
        options ??= new();
        services.AddSingleton<IRedisDriver>(new RedisDriver(connection, options.Database));
        services.TryAddSingleton<MessageProcessor>();
        services.AddSingleton(provider => new RedisStreamsSender(
            provider.GetRequiredService<IRedisDriver>(),
            options
        ));
        services.AddSingleton<IMessageSender>(provider =>
            provider.GetRequiredService<RedisStreamsSender>()
        );
        services.AddSingleton<IMessageRequestSender>(provider =>
            provider.GetRequiredService<RedisStreamsSender>()
        );
        return services;
    }

    /// <summary>The caller owns the supplied connection and disposes it after the host stops.</summary>
    public static IServiceCollection AddRedisPubSubMessaging(
        this IServiceCollection services,
        IConnectionMultiplexer connection
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connection);
        services.AddSingleton<IRedisDriver>(new RedisDriver(connection, -1));
        services.TryAddSingleton<MessageProcessor>();
        services.AddSingleton<IMessageSender>(provider => new RedisPubSubSender(
            provider.GetRequiredService<IRedisDriver>()
        ));
        return services;
    }

    public static IServiceCollection AddRedisStreamConsumer(
        this IServiceCollection services,
        RedisStreamConsumerOptions? options,
        params MessageReceiveBinding[] bindings
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(bindings);
        var definitions = bindings.ToArray();
        options ??= new();
        services.AddSingleton<IHostedService>(provider => new RedisStreamsConsumer(
            provider.GetRequiredService<IRedisDriver>(),
            definitions,
            provider.GetRequiredService<MessageProcessor>(),
            provider,
            options
        ));
        return services;
    }

    public static IServiceCollection AddRedisPubSubConsumer(
        this IServiceCollection services,
        RedisPubSubConsumerOptions? options,
        params MessageReceiveBinding[] bindings
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(bindings);
        var definitions = bindings.ToArray();
        options ??= new();
        services.AddSingleton<IHostedService>(provider => new RedisPubSubConsumer(
            provider.GetRequiredService<IRedisDriver>(),
            definitions,
            provider.GetRequiredService<MessageProcessor>(),
            provider,
            options
        ));
        return services;
    }
}
