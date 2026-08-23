using Grpc.Core;

namespace Bench.Stacks.GrpcNet;

public sealed class GrpcNetBenchmarkService
    : Bench.GrpcNet.GrpcBenchmarkService.GrpcBenchmarkServiceBase
{
    private static readonly Bench.GrpcNet.GetItemOutput GetItemResult = new()
    {
        Item = GrpcBenchmarkData.Item("item-0", 0),
    };

    private static readonly Bench.GrpcNet.ListItemsOutput ListItems100Result = CreateList(100);

    public override Task<Bench.GrpcNet.GetItemOutput> GetItem(
        Bench.GrpcNet.GetItemInput request,
        ServerCallContext context
    ) => Task.FromResult(GetItemResult);

    public override Task<Bench.GrpcNet.ListItemsOutput> ListItems(
        Bench.GrpcNet.ListItemsInput request,
        ServerCallContext context
    ) => Task.FromResult(request.Count == 100 ? ListItems100Result : CreateList(request.Count));

    private static Bench.GrpcNet.ListItemsOutput CreateList(int count)
    {
        var result = new Bench.GrpcNet.ListItemsOutput();
        result.Items.Add(
            Enumerable
                .Range(0, count)
                .Select(index => GrpcBenchmarkData.Item($"item-{index}", index))
        );
        return result;
    }
}

internal static class GrpcBenchmarkData
{
    public static Bench.GrpcNet.Item Item(string id, int index)
    {
        var item = new Bench.GrpcNet.Item
        {
            Id = id,
            Name = $"Benchmark item {index}",
            PriceCents = 1_000 + index,
            InStock = true,
        };
        item.Tags.Add(["benchmark", "grpc", $"tag-{index % 5}"]);
        return item;
    }
}
