using Xunit.Abstractions;

namespace RestJson1.Conformance;

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
        var requests = Model.EnumerateHttpRequestTests(RestJson1Allowlist.Protocol).ToList();
        var responses = Model.EnumerateHttpResponseTests(RestJson1Allowlist.Protocol).ToList();

        // Each side's denominator is the number of cases that actually apply to that side
        // (a server-only case never applies to the client and vice versa). Client and server
        // are reported separately so neither direction masks the other.
        var clientReqTotal = requests.Count(c => c.AppliesToClient);
        var clientRespTotal = responses.Count(c => c.AppliesToClient);
        var serverReqTotal = requests.Count(c => c.AppliesToServer);
        var serverRespTotal = responses.Count(c => c.AppliesToServer);

        // Count executable cases that actually apply to the side in question. Intersecting with
        // the applicable set keeps the rate honest even if an allowlist accidentally lists a
        // case from the other direction (which would otherwise push the rate over 100%).
        var execClientReq = requests.Count(c =>
            c.AppliesToClient && RestJson1Allowlist.ExecutableRequestCases.Contains(c.Id)
        );
        var execClientResp = responses.Count(c =>
            c.AppliesToClient && RestJson1Allowlist.ExecutableResponseCases.Contains(c.Id)
        );
        var execServerReq = requests.Count(c =>
            c.AppliesToServer && RestJson1Allowlist.ExecutableServerRequestCases.Contains(c.Id)
        );
        var execServerResp = responses.Count(c =>
            c.AppliesToServer && RestJson1Allowlist.ExecutableServerResponseCases.Contains(c.Id)
        );

        output.WriteLine(
            $"[{RestJson1Allowlist.Protocol}] "
                + $"client-requests: {execClientReq}/{clientReqTotal} ({Pct(execClientReq, clientReqTotal)}), "
                + $"client-responses: {execClientResp}/{clientRespTotal} ({Pct(execClientResp, clientRespTotal)}), "
                + $"server-requests: {execServerReq}/{serverReqTotal} ({Pct(execServerReq, serverReqTotal)}), "
                + $"server-responses: {execServerResp}/{serverRespTotal} ({Pct(execServerResp, serverRespTotal)})"
        );
    }

    private static string Pct(int part, int whole) =>
        whole == 0 ? "n/a" : $"{(double)part / whole * 100:0.0}%";
}
