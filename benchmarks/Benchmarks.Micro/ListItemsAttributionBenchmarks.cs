using System.Text.Json;
using Bench.Clients;
using Bench.Hosting;
using Bench.Stacks.MinimalApi;
using BenchmarkDotNet.Attributes;
using Nsmithy.Bench;
using NSmithy.Codecs.Json;
using NSmithy.Core.Serde;

namespace Bench.Micro;

/// <summary>
/// Splits a list-items client call into its parts, on one captured response: the body alone
/// through STJ source-gen and through the NSmithy codec, then the full NSmithy and NSwag client
/// calls. The client figure minus the codec figure is what the transport and runtime add.
/// </summary>
[MemoryDiagnoser]
public class ListItemsAttributionBenchmarks : IDisposable
{
    private static readonly ICodec<ListItemsOutput> Codec = JsonCodecFactory.Default.FromSchema(
        ListItemsOutputSchema.Schema
    );

    private byte[] body = null!;
    private StubTransport transport = null!;
    private IBenchClient nsmithy = null!;
    private IBenchClient nswag = null!;

    [Params(100, 10_000)]
    public int Count { get; set; }

    [GlobalSetup]
    public async Task SetupAsync()
    {
        StubResponse canned;
        await using (var server = await BenchStacks.StartNSmithyAsync())
        {
            using var capturing = new ResponseCapturingHandler(server.CreateHandler());
            await using var probe = BenchClientFactory.Create(
                BenchClientFactory.NSmithy,
                capturing
            );
            await probe.ListItemsAsync(Count);
            canned = capturing.Captured!;
        }

        body = canned.Body;
        transport = new StubTransport(_ => canned);
        nsmithy = BenchClientFactory.Create(BenchClientFactory.NSmithy, transport);
        nswag = BenchClientFactory.Create(BenchClientFactory.NSwag, transport);
    }

    [Benchmark]
    public ListItemsResponse? StjBody() =>
        JsonSerializer.Deserialize(body, MinimalApiJsonContext.Default.ListItemsResponse);

    [Benchmark]
    public ListItemsOutput NSmithyBody() => Codec.Deserialize(body);

    [Benchmark]
    public Task<BenchListResult> NSmithyClient() => nsmithy.ListItemsAsync(Count);

    [Benchmark]
    public Task<BenchListResult> NSwagClient() => nswag.ListItemsAsync(Count);

    public void Dispose()
    {
        transport?.Dispose();
        GC.SuppressFinalize(this);
    }
}
