using Bench.Corpus;
using Bench.Hosting;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Filters;

namespace Bench.Micro;

/// <summary>
/// The server suite: the full in-memory HTTP pipeline, raw request bytes in to
/// response bytes out.
/// </summary>
/// <remarks>
/// Routing, model binding, the codec, and the handler are all in the
/// measurement; sockets and Kestrel are not. This is the number that answers
/// "how fast is this stack at serving this contract", but a regression here
/// does not say <em>where</em> it is, which is what the codec suite is for.
/// <para>
/// Every stack in this comparison is verified byte-identical by the parity gate
/// on every scenario it is measured on. A stack that cannot serve a scenario
/// declares so in <see cref="BenchStacks"/>, and that pair is left out here.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[Config(typeof(ServedScenariosOnly))]
public class ServerBenchmarks
{
    private BenchServer server = null!;
    private BenchRequest request = null!;

    [ParamsSource(nameof(StackNames))]
    public string Stack { get; set; } = BenchStacks.NSmithy;

    [ParamsSource(nameof(ScenarioNames))]
    public string Scenario { get; set; } = "";

    public static IEnumerable<string> StackNames => BenchStacks.All.Select(s => s.Name);

    public static IEnumerable<string> ScenarioNames => BenchCorpus.All.Select(r => r.Name);

    [GlobalSetup]
    public async Task SetupAsync()
    {
        server = await BenchStacks.StartAsync(Stack);
        request = BenchCorpus.ByName(Scenario);

        // Fail loudly here rather than silently benchmarking an error path: a
        // stack that 500s on a scenario would otherwise look fast.
        using var probe = BenchServer.BuildRequest(request);
        using var response = await server.Client.SendAsync(probe);
        if ((int)response.StatusCode >= 500)
        {
            throw new InvalidOperationException(
                $"Stack '{Stack}' returned {(int)response.StatusCode} for scenario "
                    + $"'{Scenario}'; benchmarking it would measure the failure path."
            );
        }
    }

    [GlobalCleanup]
    public async Task CleanupAsync() => await server.DisposeAsync();

    /// <summary>One full request/response round trip through the stack.</summary>
    /// <returns>
    /// The response body length, returned so the JIT cannot elide the read.
    /// </returns>
    [Benchmark]
    public async Task<int> RoundTrip()
    {
        using var message = BenchServer.BuildRequest(request);
        using var response = await server.Client.SendAsync(message);
        var body = await response.Content.ReadAsByteArrayAsync();
        return body.Length;
    }

    private sealed class ServedScenariosOnly : ManualConfig
    {
        public ServedScenariosOnly() =>
            AddFilter(
                new SimpleFilter(benchmark =>
                    BenchStacks
                        .ByName((string)benchmark.Parameters[nameof(Stack)])
                        .Serves((string)benchmark.Parameters[nameof(Scenario)])
                )
            );
    }
}
