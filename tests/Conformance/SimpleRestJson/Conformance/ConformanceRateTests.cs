using Xunit.Abstractions;

namespace SimpleRestJson.Conformance;

/// <summary>
/// Reports the current pass rate of this protocol's conformance suite against the official
/// Smithy/alloy protocol test fixtures. Always passes; the numbers show up in the test logs
/// and in CI output so coverage trends are visible without a separate generated doc.
/// </summary>
public sealed class ConformanceRateTests(ITestOutputHelper output)
{
    private static readonly SmithyTestModel Model = SmithyTestModel.Load();

    [Fact]
    public void ReportConformanceRate()
    {
        var totalRequests = Model
            .EnumerateHttpRequestTests(SimpleRestJsonAllowlist.Protocol)
            .Count();
        var totalResponses = Model
            .EnumerateHttpResponseTests(SimpleRestJsonAllowlist.Protocol)
            .Count();
        var execRequests = SimpleRestJsonAllowlist.ExecutableRequestCases.Count;
        var execResponses = SimpleRestJsonAllowlist.ExecutableResponseCases.Count;

        output.WriteLine(
            $"[{SimpleRestJsonAllowlist.Protocol}] requests: {execRequests}/{totalRequests} ({Pct(execRequests, totalRequests)}), responses: {execResponses}/{totalResponses} ({Pct(execResponses, totalResponses)})"
        );
    }

    private static string Pct(int part, int whole) =>
        whole == 0 ? "n/a" : $"{(double)part / whole * 100:0.0}%";
}
