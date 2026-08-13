using Xunit.Abstractions;

namespace RpcV2Cbor.Conformance;

/// <summary>
/// Reports the current pass rate of this protocol's conformance suite against the official
/// Smithy protocol test fixtures.
/// </summary>
public sealed class ConformanceRateTests(ITestOutputHelper output)
{
    private static readonly SmithyTestModel Model = SmithyTestModel.Load();

    [Fact]
    public void ReportConformanceRate()
    {
        var requests = Model.EnumerateHttpRequestTests(RpcV2CborAllowlist.Protocol).ToList();
        var responses = Model.EnumerateHttpResponseTests(RpcV2CborAllowlist.Protocol).ToList();

        // Denominator is the number of cases that actually apply to each side (client / server),
        // not the full corpus — server-only cases never apply to the client and vice versa.
        var clientReqTotal = requests.Count(c => c.AppliesToClient);
        var clientRespTotal = responses.Count(c => c.AppliesToClient);
        var serverReqTotal = requests.Count(c => c.AppliesToServer);
        var serverRespTotal = responses.Count(c => c.AppliesToServer);

        // Count executable cases that actually apply to the side in question, so the rate stays
        // honest even if an allowlist accidentally lists a case from the other direction.
        var execRequests = requests.Count(c =>
            c.AppliesToClient && RpcV2CborAllowlist.ExecutableRequestCases.Contains(c.Id)
        );
        // Cases quarantined in KnownParamGaps are subtracted here. They are listed as
        // executable but no longer run, and counting them would overstate the rate on the
        // docs Protocol Status page — which is the number this test exists to produce.
        var execResponses = responses.Count(c =>
            c.AppliesToClient
            && RpcV2CborAllowlist.ExecutableResponseCases.Contains(c.Id)
            && !KnownParamGaps.Response.Contains(c.Id)
        );
        var execServerRequests = requests.Count(c =>
            c.AppliesToServer
            && RpcV2CborAllowlist.ExecutableServerRequestCases.Contains(c.Id)
            && !KnownParamGaps.ServerRequest.Contains(c.Id)
        );
        var execServerResponses = responses.Count(c =>
            c.AppliesToServer && RpcV2CborAllowlist.ExecutableServerResponseCases.Contains(c.Id)
        );

        output.WriteLine(
            $"[{RpcV2CborAllowlist.Protocol}] "
                + $"client-requests: {execRequests}/{clientReqTotal} ({Pct(execRequests, clientReqTotal)}), "
                + $"client-responses: {execResponses}/{clientRespTotal} ({Pct(execResponses, clientRespTotal)}), "
                + $"server-requests: {execServerRequests}/{serverReqTotal} ({Pct(execServerRequests, serverReqTotal)}), "
                + $"server-responses: {execServerResponses}/{serverRespTotal} ({Pct(execServerResponses, serverRespTotal)})"
        );
    }

    private static string Pct(int part, int whole) =>
        whole == 0 ? "n/a" : $"{(double)part / whole * 100:0.0}%";
}
