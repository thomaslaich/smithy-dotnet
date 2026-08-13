using Bench.Corpus;
using Bench.Hosting;

namespace Bench.Parity;

/// <summary>
/// The gate that makes the benchmark numbers mean something.
/// </summary>
/// <remarks>
/// A comparison between stacks serving subtly different contracts is not a
/// comparison, the stack doing less work wins. Every stack must answer every
/// corpus scenario with the same status, contract-relevant headers, and
/// byte-identical body. The golden captures under
/// <c>benchmarks/contract/golden/</c> are committed so a contract change is a
/// reviewable diff; regenerate with <c>just bench-capture</c>.
/// </remarks>
public sealed class ContractParityTests
{
    public static TheoryData<string, string> StackScenarioMatrix
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var (stack, _) in BenchStacks.All)
            {
                foreach (var request in BenchCorpus.All)
                    data.Add(stack, request.Name);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(StackScenarioMatrix))]
    public async Task StackMatchesGoldenContract(string stack, string scenario)
    {
        var goldenPath = GoldenPath(scenario);
        Assert.True(
            File.Exists(goldenPath),
            $"No golden capture for scenario '{scenario}'. Run `just bench-capture` to record one."
        );

        var expected = WireCapture.FromJson(await File.ReadAllTextAsync(goldenPath));

        await using var server = await BenchStacks.StartAsync(stack);
        var actual = await WireCapture.CaptureAsync(server, BenchCorpus.ByName(scenario));

        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.Headers, actual.Headers);

        // Compare the hash rather than the body: bodies run to megabytes, and a
        // failed Assert.Equal on those produces an unreadable diff.
        Assert.Equal(expected.BodyLength, actual.BodyLength);
        Assert.Equal(expected.BodySha256, actual.BodySha256);
    }

    /// <summary>
    /// Guards the corpus itself. A scenario every stack answers with 4xx/5xx would
    /// pass the parity check above while benchmarking nothing but an error path.
    /// </summary>
    [Theory]
    [MemberData(nameof(StackScenarioMatrix))]
    public async Task StackDoesNotFailScenario(string stack, string scenario)
    {
        await using var server = await BenchStacks.StartAsync(stack);
        var capture = await WireCapture.CaptureAsync(server, BenchCorpus.ByName(scenario));

        // Three deliberate non-2xx families; everything else must succeed. A 5xx
        // is never expected, and is called out separately because it would mean
        // the scenario is measuring an unhandled failure rather than the path it
        // is named for.
        Assert.InRange(capture.Status, 200, 499);

        var expected = scenario switch
        {
            _ when scenario == BenchCorpus.GetItemMiss.Name => 404,
            _ when scenario.StartsWith("validation-", StringComparison.Ordinal) => 400,
            _ => 0,
        };

        if (expected != 0)
            Assert.Equal(expected, capture.Status);
        else
            Assert.InRange(capture.Status, 200, 299);
    }

    private static string GoldenPath(string scenario) =>
        Path.Combine(RepoRelative("benchmarks", "contract", "golden"), $"{scenario}.json");

    /// <summary>
    /// Resolves a path relative to the repository root by walking up from the test
    /// assembly until the benchmarks directory is found.
    /// </summary>
    private static string RepoRelative(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "benchmarks")))
            dir = dir.Parent;

        if (dir is null)
        {
            throw new InvalidOperationException(
                "Could not locate the repository root from the test assembly location."
            );
        }

        return Path.Combine([dir.FullName, .. segments]);
    }
}
