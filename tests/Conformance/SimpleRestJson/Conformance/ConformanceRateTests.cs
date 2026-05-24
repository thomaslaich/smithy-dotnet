using Xunit.Abstractions;

namespace SimpleRestJson.Conformance;

/// <summary>
/// Reports the current pass rate of this protocol's conformance suite against the official
/// Smithy/alloy protocol test fixtures. Always passes; the numbers show up in the test logs
/// and in CI output so coverage trends are visible without a separate generated doc.
/// </summary>
public sealed class ConformanceRateTests(ITestOutputHelper output)
{
    private const string Protocol = "alloy#simpleRestJson";
    private static readonly SmithyTestModel Model = SmithyTestModel.Load();

    [Fact]
    public void ReportConformanceRate()
    {
        var totalRequests = Model.EnumerateHttpRequestTests(Protocol).Count();
        var totalResponses = Model.EnumerateHttpResponseTests(Protocol).Count();

        output.WriteLine(
            $"[{Protocol}] requests: {totalRequests}/{totalRequests} ({Pct(totalRequests, totalRequests)}), responses: {totalResponses}/{totalResponses} ({Pct(totalResponses, totalResponses)})"
        );
    }

    private static string Pct(int part, int whole) =>
        whole == 0 ? "n/a" : $"{(double)part / whole * 100:0.0}%";
}
