using Xunit.Abstractions;

namespace RestXml.Conformance;

/// <summary>
/// Reports the current pass rate of this protocol's conformance suite against the official
/// Smithy/AWS protocol test fixtures.
/// </summary>
public sealed class ConformanceRateTests(ITestOutputHelper output)
{
    private static readonly SmithyTestModel Model = SmithyTestModel.Load();

    [Fact]
    public void ReportConformanceRate()
    {
        var totalRequests = Model.EnumerateHttpRequestTests(RestXmlAllowlist.Protocol).Count();
        var totalResponses = Model.EnumerateHttpResponseTests(RestXmlAllowlist.Protocol).Count();
        var execRequests = RestXmlAllowlist.ExecutableRequestCases.Count;
        var execResponses = RestXmlAllowlist.ExecutableResponseCases.Count;

        output.WriteLine(
            $"[{RestXmlAllowlist.Protocol}] requests: {execRequests}/{totalRequests} ({Pct(execRequests, totalRequests)}), responses: {execResponses}/{totalResponses} ({Pct(execResponses, totalResponses)})"
        );
    }

    private static string Pct(int part, int whole) =>
        whole == 0 ? "n/a" : $"{(double)part / whole * 100:0.0}%";
}
