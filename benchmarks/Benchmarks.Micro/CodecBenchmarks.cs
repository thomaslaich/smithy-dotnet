using System.Text.Json;
using Bench.Corpus;
using Bench.Domain;
using Bench.Stacks.MinimalApi;
using BenchmarkDotNet.Attributes;
using Nsmithy.Bench;
using NSmithy.Codecs.Json;
using NSmithy.Core.Serde;

namespace Bench.Micro;

/// <summary>
/// The codec suite, write side: typed object to bytes, with no ASP.NET in the
/// measurement.
/// </summary>
/// <remarks>
/// The server suite tells you a stack got slower. This tells you whether the codec is
/// why. Nothing here touches routing, model binding, DI, or the HTTP pipeline,
/// and the domain-to-DTO mapping happens once in setup rather than per
/// iteration, so what remains is serialization alone.
/// <para>
/// The System.Text.Json source-generated path is the baseline, so the ratio
/// column reads directly as "how many times the hand-written ceiling this
/// costs".
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class SerializationBenchmarks
{
    private static readonly IJsonCodec<ListItemsOutput> ListCodec = JsonCodec.FromSchema(
        ListItemsOutputSchema.Schema
    );

    private ListItemsOutput smithyList = null!;
    private ListItemsResponse stjList = null!;

    /// <summary>Response element count. Separates fixed cost from per-element cost.</summary>
    [Params(1, 100, 10_000)]
    public int ItemCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var items = BenchDomain.ListItems(ItemCount);

        var smithySummaries = new ItemSummary[items.Count];
        var stjSummaries = new ItemSummaryDto[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            smithySummaries[i] = new ItemSummary(
                InStock: item.InStock,
                ItemId: item.ItemId,
                Name: item.Name,
                PriceCents: item.PriceCents,
                Category: item.Category,
                Tags: item.Tags is null ? null : new StringList(item.Tags)
            );
            stjSummaries[i] = new ItemSummaryDto
            {
                ItemId = item.ItemId,
                Name = item.Name,
                PriceCents = item.PriceCents,
                InStock = item.InStock,
                Category = item.Category,
                Tags = item.Tags,
            };
        }

        smithyList = new ListItemsOutput(new ItemSummaries(smithySummaries));
        stjList = new ListItemsResponse { Items = stjSummaries };
    }

    [Benchmark(Baseline = true, Description = "STJ source-gen")]
    public byte[] Stj() =>
        JsonSerializer.SerializeToUtf8Bytes(
            stjList,
            MinimalApiJsonContext.Default.ListItemsResponse
        );

    [Benchmark(Description = "NSmithy schema codec")]
    public byte[] Smithy() => ListCodec.Serialize(smithyList);
}

/// <summary>
/// The codec suite, read side: bytes to typed object.
/// </summary>
/// <remarks>
/// Uses the corpus order bodies directly, so the bytes parsed here are the same
/// bytes the server benchmarks post.
/// </remarks>
[MemoryDiagnoser]
public class DeserializationBenchmarks
{
    private static readonly IJsonCodec<CreateOrderInput> OrderCodec = JsonCodec.FromSchema(
        CreateOrderInputSchema.Schema
    );

    private byte[] payload = null!;

    /// <summary>Corpus scenario supplying the request body: small, or ~1 MB.</summary>
    [Params("create-order-small", "create-order-large")]
    public string Scenario { get; set; } = "create-order-small";

    [GlobalSetup]
    public void Setup() =>
        payload =
            BenchCorpus.ByName(Scenario).Body
            ?? throw new InvalidOperationException($"Scenario '{Scenario}' has no request body.");

    [Benchmark(Baseline = true, Description = "STJ source-gen")]
    public CreateOrderRequest? Stj() =>
        JsonSerializer.Deserialize(payload, MinimalApiJsonContext.Default.CreateOrderRequest);

    [Benchmark(Description = "NSmithy schema codec")]
    public CreateOrderInput Smithy() => OrderCodec.Deserialize(payload);
}
