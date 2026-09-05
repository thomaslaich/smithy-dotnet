using Examples.Redis.Inventory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSmithy.Messaging.Redis;
using StackExchange.Redis;

var redis = args.Length > 0 ? args[0] : "localhost:6379";
await using var connection = await ConnectionMultiplexer.ConnectAsync(redis);
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddRedisStreamsMessaging(connection);
builder.Services.AddInventoryCommandConsumer(
    new RedisStreamConsumerOptions
    {
        ConsumerGroup = "redis-inventory-owner",
        ConsumerName = $"server-{Environment.ProcessId}",
    }
);
builder.Services.AddScoped<IGetStockHandler, InventoryOwner>();

Console.WriteLine($"Inventory server listening on {redis}. Ctrl+C stops.");
await builder.Build().RunAsync();

sealed class InventoryOwner : IGetStockHandler
{
    public Task<GetStockOutput> HandleAsync(
        GetStockInput command,
        CancellationToken cancellationToken = default
    )
    {
        Console.WriteLine($"GetStock: {command.ProductId}");
        return Task.FromResult(new GetStockOutput(Available: 42, ProductId: command.ProductId));
    }
}
