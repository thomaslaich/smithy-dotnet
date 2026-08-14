using Xunit.Abstractions;

namespace AwsJson.Conformance;

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
        var requests = Model.EnumerateHttpRequestTests(AwsJsonAllowlist.Protocol).ToList();
        var responses = Model.EnumerateHttpResponseTests(AwsJsonAllowlist.Protocol).ToList();

        var clientReqTotal = requests.Count(c => c.AppliesToClient);
        var clientRespTotal = responses.Count(c => c.AppliesToClient);

        var execClientReq = requests.Count(c =>
            c.AppliesToClient && AwsJsonAllowlist.ExecutableRequestCases.Contains(c.Id)
        );
        // Cases quarantined in KnownParamGaps are subtracted here. They are listed as
        // executable but no longer run, and counting them would overstate the rate on the
        // docs Protocol Status page — which is the number this test exists to produce.
        var execClientResp = responses.Count(c =>
            c.AppliesToClient
            && AwsJsonAllowlist.ExecutableResponseCases.Contains(c.Id)
            && !KnownParamGaps.Response.Contains(c.Id)
        );

        output.WriteLine(
            $"[{AwsJsonAllowlist.Protocol}] "
                + $"client-requests: {execClientReq}/{clientReqTotal} ({Pct(execClientReq, clientReqTotal)}), "
                + $"client-responses: {execClientResp}/{clientRespTotal} ({Pct(execClientResp, clientRespTotal)})"
        );
    }

    private static string Pct(int part, int whole) =>
        whole == 0 ? "n/a" : $"{(double)part / whole * 100:0.0}%";
}
