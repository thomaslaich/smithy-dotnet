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
        var requests = Model.EnumerateHttpRequestTests(Protocol).ToList();
        var responses = Model.EnumerateHttpResponseTests(Protocol).ToList();

        // Both the generated client and server drive every applicable case (no allowlist),
        // minus the cases quarantined in KnownParamGaps. Reporting the totals as if they all
        // executed printed a flat 100% while 12 cases were not running at all.
        var clientReqTotal = requests.Count(c => c.AppliesToClient);
        var clientRespTotal = responses.Count(c => c.AppliesToClient);
        var serverReqTotal = requests.Count(c => c.AppliesToServer);
        var serverRespTotal = responses.Count(c => c.AppliesToServer);

        var execClientReq = clientReqTotal;
        var execClientResp = responses.Count(c =>
            c.AppliesToClient && !KnownParamGaps.Response.Contains(c.Id)
        );
        var execServerReq = requests.Count(c =>
            c.AppliesToServer && !KnownParamGaps.ServerRequest.Contains(c.Id)
        );
        var execServerResp = serverRespTotal;

        output.WriteLine(
            $"[{Protocol}] "
                + $"client-requests: {execClientReq}/{clientReqTotal} ({Pct(execClientReq, clientReqTotal)}), "
                + $"client-responses: {execClientResp}/{clientRespTotal} ({Pct(execClientResp, clientRespTotal)}), "
                + $"server-requests: {execServerReq}/{serverReqTotal} ({Pct(execServerReq, serverReqTotal)}), "
                + $"server-responses: {execServerResp}/{serverRespTotal} ({Pct(execServerResp, serverRespTotal)})"
        );
    }

    private static string Pct(int part, int whole) =>
        whole == 0 ? "n/a" : $"{(double)part / whole * 100:0.0}%";
}
