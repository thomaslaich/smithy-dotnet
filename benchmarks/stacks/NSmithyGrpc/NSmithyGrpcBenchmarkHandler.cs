using Nsmithy.Bench.Grpc;

namespace Bench.Stacks.NSmithyGrpc;

public sealed class NSmithyGrpcBenchmarkHandler : IGrpcBenchmarkServiceHandler
{
    private static readonly GetItemOutput GetItemResult = new(GrpcBenchmarkData.Item("item-0", 0));
    private static readonly ListItemsOutput ListItems100Result = new(
        new ItemList(
            Enumerable.Range(0, 100).Select(index => GrpcBenchmarkData.Item($"item-{index}", index))
        )
    );

    public Task<GetItemOutput> GetItemAsync(
        GetItemInput input,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(GetItemResult);

    public Task<ListItemsOutput> ListItemsAsync(
        ListItemsInput input,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(input.Count == 100 ? ListItems100Result : CreateList(input.Count));

    private static ListItemsOutput CreateList(int count) =>
        new(
            new ItemList(
                Enumerable
                    .Range(0, count)
                    .Select(index => GrpcBenchmarkData.Item($"item-{index}", index))
            )
        );
}

internal static class GrpcBenchmarkData
{
    public static Item Item(string id, int index) =>
        new(
            Id: id,
            Name: $"Benchmark item {index}",
            PriceCents: 1_000 + index,
            InStock: true,
            Tags: new TagList(["benchmark", "grpc", $"tag-{index % 5}"])
        );
}
