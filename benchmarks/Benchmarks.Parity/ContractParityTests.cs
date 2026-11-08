using Bench.Corpus;
using Bench.Hosting;

namespace Bench.Parity;

/// <summary>
/// The gate that makes the benchmark numbers mean something.
/// </summary>
/// <remarks>
/// A comparison between stacks serving subtly different contracts is not a
/// comparison, the stack doing less work wins. Every stack must answer every
/// corpus scenario it is compared on with the same status, contract-relevant
/// headers, and byte-identical body. The golden captures under
/// <c>benchmarks/contract/golden/</c> are committed so a contract change is a
/// reviewable diff; regenerate with <c>just bench-capture</c>.
/// <para>
/// A stack may declare scenarios it cannot serve. Those are skipped here and in
/// the server suite, and <see cref="UnservedScenarioStillDiffers"/> keeps the
/// declaration honest: the moment a stack starts serving one of them, the test
/// fails until the declaration is removed.
/// </para>
/// </remarks>
public sealed class ContractParityTests
{
    public static TheoryData<string, string> ServedMatrix => Matrix(served: true);

    public static TheoryData<string, string> UnservedMatrix => Matrix(served: false);

    [Theory]
    [MemberData(nameof(ServedMatrix))]
    public async Task StackMatchesGoldenContract(string stack, string scenario)
    {
        var expected = await ReadGoldenAsync(scenario);

        await using var server = await BenchStacks.StartAsync(stack);
        var actual = await WireCapture.CaptureAsync(server, BenchCorpus.ByName(scenario));

        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.Headers, actual.Headers);

        // Compare the hash rather than the body: bodies run to megabytes, and a
        // failed Assert.Equal on those produces an unreadable diff.
        Assert.Equal(expected.BodyLength, actual.BodyLength);
        Assert.Equal(expected.BodySha256, actual.BodySha256);
    }

    [Theory]
    [MemberData(nameof(UnservedMatrix))]
    public async Task UnservedScenarioStillDiffers(string stack, string scenario)
    {
        var expected = await ReadGoldenAsync(scenario);

        await using var server = await BenchStacks.StartAsync(stack);
        WireCapture actual;
        try
        {
            actual = await WireCapture.CaptureAsync(server, BenchCorpus.ByName(scenario));
        }
        catch (Exception)
        {
            // The in-memory host rethrows an unhandled exception to the caller.
            // A stack that cannot even answer the scenario certainly differs.
            return;
        }

        var matches =
            expected.Status == actual.Status
            && expected.Headers.SequenceEqual(actual.Headers)
            && expected.BodyLength == actual.BodyLength
            && expected.BodySha256 == actual.BodySha256;

        Assert.False(
            matches,
            $"Stack '{stack}' now serves '{scenario}' byte-identically. "
                + "Remove it from the stack's unserved scenarios in BenchStacks."
        );
    }

    /// <summary>
    /// Guards the corpus itself. A scenario every stack answers with 4xx/5xx would
    /// pass the parity check above while benchmarking nothing but an error path.
    /// </summary>
    [Theory]
    [MemberData(nameof(ServedMatrix))]
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

    private static TheoryData<string, string> Matrix(bool served)
    {
        var data = new TheoryData<string, string>();
        foreach (var stack in BenchStacks.All)
        {
            foreach (var request in BenchCorpus.All)
            {
                if (stack.Serves(request.Name) == served)
                    data.Add(stack.Name, request.Name);
            }
        }

        return data;
    }

    private static async Task<WireCapture> ReadGoldenAsync(string scenario)
    {
        var goldenPath = Path.Combine(
            RepoRelative("benchmarks", "contract", "golden"),
            $"{scenario}.json"
        );
        Assert.True(
            File.Exists(goldenPath),
            $"No golden capture for scenario '{scenario}'. Run `just bench-capture` to record one."
        );

        return WireCapture.FromJson(await File.ReadAllTextAsync(goldenPath));
    }

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
