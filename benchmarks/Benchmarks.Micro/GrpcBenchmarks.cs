using Bench.Hosting;
using BenchmarkDotNet.Attributes;
using Google.Protobuf;
using NSmithy.Codecs.Proto;
using Native = Nsmithy.Bench.Grpc;

namespace Bench.Micro;

/// <summary>
/// Unary gRPC client cost with a canned HTTP/2 response and no server in the measurement.
/// </summary>
[MemoryDiagnoser]
public class GrpcClientBenchmarks
{
    private GrpcBenchClient grpcNet = null!;
    private GrpcBenchClient nsmithy = null!;

    [ParamsSource(nameof(ScenarioNames))]
    public string Scenario { get; set; } = "";

    public static IEnumerable<string> ScenarioNames => GrpcBenchScenarios.All.Select(s => s.Name);

    [GlobalSetup]
    public async Task SetupAsync()
    {
        var scenario = GrpcBenchScenarios.ByName(Scenario);
        grpcNet = GrpcBenchClient.Create(
            GrpcBenchStacks.GrpcNet,
            scenario,
            new GrpcCannedResponseHandler(scenario)
        );
        nsmithy = GrpcBenchClient.Create(
            GrpcBenchStacks.NSmithy,
            scenario,
            new GrpcCannedResponseHandler(scenario)
        );

        var expected = Scenario == "get-item" ? 1_000 : 100;
        if (await grpcNet.InvokeAsync() != expected || await nsmithy.InvokeAsync() != expected)
        {
            throw new InvalidOperationException(
                $"A client did not return the expected result for '{Scenario}'."
            );
        }
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        await grpcNet.DisposeAsync();
        await nsmithy.DisposeAsync();
    }

    [Benchmark(Baseline = true, Description = "Grpc.Net client")]
    public Task<int> GrpcNet() => grpcNet.InvokeAsync();

    [Benchmark(Description = "NSmithy gRPC client")]
    public Task<int> NSmithy() => nsmithy.InvokeAsync();
}

/// <summary>
/// Unary gRPC server cost through the full in-memory ASP.NET Core pipeline, excluding sockets.
/// </summary>
[MemoryDiagnoser]
public class GrpcServerBenchmarks
{
    private GrpcBenchServer grpcNet = null!;
    private GrpcBenchServer nsmithy = null!;
    private GrpcBenchScenario scenario = null!;

    [ParamsSource(nameof(ScenarioNames))]
    public string Scenario { get; set; } = "";

    public static IEnumerable<string> ScenarioNames => GrpcBenchScenarios.All.Select(s => s.Name);

    [GlobalSetup]
    public async Task SetupAsync()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        scenario = GrpcBenchScenarios.ByName(Scenario);
        grpcNet = GrpcBenchStacks.Start(GrpcBenchStacks.GrpcNet);
        nsmithy = GrpcBenchStacks.Start(GrpcBenchStacks.NSmithy);

        await ValidateRoundTrip(grpcNet);
        await ValidateRoundTrip(nsmithy);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        grpcNet.Dispose();
        nsmithy.Dispose();
    }

    [Benchmark(Baseline = true, Description = "Grpc.AspNetCore server")]
    public Task<int> GrpcNet() => RoundTrip(grpcNet);

    [Benchmark(Description = "NSmithy gRPC server")]
    public Task<int> NSmithy() => RoundTrip(nsmithy);

    private async Task<int> RoundTrip(GrpcBenchServer server)
    {
        using var request = scenario.CreateRequest();
        using var response = await server.Client.SendAsync(request);
        return (await response.Content.ReadAsByteArrayAsync()).Length;
    }

    private async Task ValidateRoundTrip(GrpcBenchServer server)
    {
        using var request = scenario.CreateRequest();
        using var response = await server.Client.SendAsync(request);
        var body = await response.Content.ReadAsByteArrayAsync();
        if (!response.IsSuccessStatusCode || !body.AsSpan().SequenceEqual(scenario.ResponseBody))
        {
            throw new InvalidOperationException(
                $"Stack '{server.Name}' did not return the expected gRPC response for '{Scenario}'."
            );
        }
    }
}

/// <summary>Protobuf response serialization without gRPC framing or HTTP.</summary>
[MemoryDiagnoser]
public class GrpcSerializationBenchmarks
{
    private GrpcCodecCase codecCase = null!;

    [ParamsSource(nameof(ScenarioNames))]
    public string Scenario { get; set; } = "";

    public static IEnumerable<string> ScenarioNames => GrpcBenchScenarios.All.Select(s => s.Name);

    [GlobalSetup]
    public void Setup() => codecCase = GrpcCodecCases.Create(Scenario);

    [Benchmark(Baseline = true, Description = "Google.Protobuf serialize")]
    public byte[] GoogleProtobuf() => codecCase.GoogleSerialize();

    [Benchmark(Description = "NSmithy Proto serialize")]
    public byte[] NSmithy() => codecCase.NSmithySerialize();
}

/// <summary>Protobuf response deserialization without gRPC framing or HTTP.</summary>
[MemoryDiagnoser]
public class GrpcDeserializationBenchmarks
{
    private GrpcCodecCase codecCase = null!;

    [ParamsSource(nameof(ScenarioNames))]
    public string Scenario { get; set; } = "";

    public static IEnumerable<string> ScenarioNames => GrpcBenchScenarios.All.Select(s => s.Name);

    [GlobalSetup]
    public void Setup() => codecCase = GrpcCodecCases.Create(Scenario);

    [Benchmark(Baseline = true, Description = "Google.Protobuf deserialize")]
    public int GoogleProtobuf() => codecCase.GoogleDeserialize();

    [Benchmark(Description = "NSmithy Proto deserialize")]
    public int NSmithy() => codecCase.NSmithyDeserialize();
}

internal sealed record GrpcCodecCase(
    Func<byte[]> GoogleSerialize,
    Func<byte[]> NSmithySerialize,
    Func<int> GoogleDeserialize,
    Func<int> NSmithyDeserialize
);

internal static class GrpcCodecCases
{
    public static GrpcCodecCase Create(string scenarioName)
    {
        var scenario = GrpcBenchScenarios.ByName(scenarioName);
        var payload = scenario.ResponseBody.AsSpan(5).ToArray();
        return scenarioName switch
        {
            "get-item" => CreateGetItem(payload),
            "list-items-100" => CreateListItems(payload),
            _ => throw new ArgumentOutOfRangeException(nameof(scenarioName)),
        };
    }

    private static GrpcCodecCase CreateGetItem(byte[] payload)
    {
        var googleValue = Bench.GrpcNet.GetItemOutput.Parser.ParseFrom(payload);
        var nsmithyValue = new Native.GetItemOutput(Item("item-0", 0));
        var nsmithyCodec = ProtoCodecFactory.Default.FromSchema(Native.GetItemOutputSchema.Schema);
        return new GrpcCodecCase(
            googleValue.ToByteArray,
            () => nsmithyCodec.Serialize(nsmithyValue),
            () => Bench.GrpcNet.GetItemOutput.Parser.ParseFrom(payload).Item.PriceCents,
            () => nsmithyCodec.Deserialize(payload).Item.PriceCents
        );
    }

    private static GrpcCodecCase CreateListItems(byte[] payload)
    {
        var googleValue = Bench.GrpcNet.ListItemsOutput.Parser.ParseFrom(payload);
        var nsmithyValue = new Native.ListItemsOutput(
            new Native.ItemList(
                Enumerable.Range(0, 100).Select(index => Item($"item-{index}", index))
            )
        );
        var nsmithyCodec = ProtoCodecFactory.Default.FromSchema(
            Native.ListItemsOutputSchema.Schema
        );
        return new GrpcCodecCase(
            googleValue.ToByteArray,
            () => nsmithyCodec.Serialize(nsmithyValue),
            () => Bench.GrpcNet.ListItemsOutput.Parser.ParseFrom(payload).Items.Count,
            () => nsmithyCodec.Deserialize(payload).Items.Values.Count
        );
    }

    private static Native.Item Item(string id, int index) =>
        new(
            Id: id,
            Name: $"Benchmark item {index}",
            PriceCents: 1_000 + index,
            InStock: true,
            Tags: new Native.TagList(["benchmark", "grpc", $"tag-{index % 5}"])
        );
}
