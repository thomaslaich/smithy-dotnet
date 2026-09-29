using Examples.Redis.Inventory;
using NSmithy.Messaging.Redis;
using StackExchange.Redis;

var redis = args.Length > 0 ? args[0] : "localhost:6379";
var productId = args.Length > 1 ? args[1] : "coffee-beans";
await using var connection = await ConnectionMultiplexer.ConnectAsync(redis);
var sender = new RedisStreamsSender(
    connection,
    new RedisMessagingOptions { RequestTimeout = TimeSpan.FromSeconds(5) }
);
var inventory = new InventoryClient(sender);
var reply = await inventory.GetStockAsync(new GetStockInput(ProductId: productId));
Console.WriteLine($"{reply.ProductId}: {reply.Available} available");
