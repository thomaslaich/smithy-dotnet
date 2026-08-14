using Bench.Corpus;
using Bench.Hosting;

// Records the reference stack's exact wire output for every corpus scenario into
// benchmarks/contract/golden/. Those files are the contract the parity gate holds
// every other stack to, and they are committed so a contract change shows up as a
// reviewable diff rather than a silently moved goalpost.
//
// Usage:
//   dotnet run --project benchmarks/Benchmarks.Capture -- [--check] [--stack <name>]
//
//   --check  compare against the committed golden files and exit non-zero on any
//            difference, without rewriting them.

var check = args.Contains("--check");
var stackIndex = Array.IndexOf(args, "--stack");
var stackName =
    stackIndex >= 0 && stackIndex + 1 < args.Length ? args[stackIndex + 1] : BenchStacks.NSmithy;

var goldenDir = Path.Combine(
    AppContext.BaseDirectory,
    "..",
    "..",
    "..",
    "..",
    "contract",
    "golden"
);
goldenDir = Path.GetFullPath(goldenDir);
Directory.CreateDirectory(goldenDir);

await using var server = await BenchStacks.StartAsync(stackName);
Console.WriteLine($"Reference stack: {server.Name}");
Console.WriteLine($"Golden directory: {goldenDir}");
Console.WriteLine();

var differences = 0;

foreach (var request in BenchCorpus.All)
{
    var capture = await WireCapture.CaptureAsync(server, request);
    var path = Path.Combine(goldenDir, $"{request.Name}.json");
    var json = capture.ToJson();
    var sizes = $"req={request.BodyBytes}B resp={capture.BodyLength}B";

    if (check)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"MISSING  {request.Name} (no golden file)");
            differences++;
            continue;
        }

        var expected = await File.ReadAllTextAsync(path);
        if (expected.ReplaceLineEndings() == json.ReplaceLineEndings())
        {
            Console.WriteLine($"ok       {capture.Describe()}");
        }
        else
        {
            Console.WriteLine($"DIFFERS  {capture.Describe()}");
            differences++;
        }
    }
    else
    {
        await File.WriteAllTextAsync(path, json);
        Console.WriteLine($"wrote    {capture.Describe()} [{sizes}]");
    }
}

if (check && differences > 0)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine(
        $"{differences} scenario(s) differ from the committed golden files. "
            + "Re-run without --check to accept the new contract, and review the diff."
    );
    return 1;
}

return 0;
