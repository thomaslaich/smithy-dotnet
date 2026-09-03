using Examples.Redis.Inventory;
using StackExchange.Redis;

var redis = args.Length > 0 ? args[0] : "localhost:6379";
await using var connection = await ConnectionMultiplexer.ConnectAsync(redis);

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

var server = new InventoryRedisStreamsConsumer(
    connection,
    new InventoryOwner(),
    "redis-inventory-owner",
    $"server-{Environment.ProcessId}"
);

Console.WriteLine($"Inventory server listening on {redis}. Ctrl+C stops.");
try
{
    await server.RunAsync(cancellationToken: cancellation.Token);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }

sealed class InventoryOwner : IInventoryRedisStreamsHandler
{
    public Task<GetStockOutput> HandleGetStockAsync(
        GetStockInput command,
        CancellationToken cancellationToken = default
    )
    {
        Console.WriteLine($"GetStock: {command.ProductId}");
        return Task.FromResult(new GetStockOutput(Available: 42, ProductId: command.ProductId));
    }
}
