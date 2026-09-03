using Examples.Redis.Inventory;
using StackExchange.Redis;

var redis = args.Length > 0 ? args[0] : "localhost:6379";
var productId = args.Length > 1 ? args[1] : "coffee-beans";
await using var connection = await ConnectionMultiplexer.ConnectAsync(redis);
var inventory = new InventoryRedisStreams(connection);

var reply = await inventory.GetStockAsync(
    new GetStockInput(ProductId: productId),
    timeout: TimeSpan.FromSeconds(5)
);
Console.WriteLine($"{reply.ProductId}: {reply.Available} available");
