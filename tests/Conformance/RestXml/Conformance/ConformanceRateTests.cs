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
        // Cases quarantined in KnownParamGaps are subtracted here. They are listed as
        // executable but no longer run, and counting them would overstate the rate on the
        // docs Protocol Status page — which is the number this test exists to produce.
        var execResponses = RestXmlAllowlist.ExecutableResponseCases.Count(id =>
            !KnownParamGaps.Response.Contains(id)
        );

        output.WriteLine(
            $"[{RestXmlAllowlist.Protocol}] requests: {execRequests}/{totalRequests} ({Pct(execRequests, totalRequests)}), responses: {execResponses}/{totalResponses} ({Pct(execResponses, totalResponses)})"
        );
    }

    private static string Pct(int part, int whole) =>
        whole == 0 ? "n/a" : $"{(double)part / whole * 100:0.0}%";
}
