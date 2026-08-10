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

        // Client still runs a curated allowlist; the server runs every applicable case whose
        // operation is part of the generated service (auxiliary services like Glacier ship
        // fixtures but no generated handler, so they are out of scope rather than failing).
        var execClientReq = requests.Count(c =>
            c.AppliesToClient && RestJson1Allowlist.ExecutableRequestCases.Contains(c.Id)
        );
        var execClientResp = responses.Count(c =>
            c.AppliesToClient && RestJson1Allowlist.ExecutableResponseCases.Contains(c.Id)
        );
        var execServerReq = requests.Count(c =>
            c.AppliesToServer && GeneratedService.HasHandler(c.OperationName)
        );
        var execServerResp = responses.Count(c => c.AppliesToServer);

        // Malformed cases are server-only — a client would never send one — and are counted after
        // testParameters expansion, because that is what actually runs. The gap between executed
        // and total is the part of the suite asserting that unparseable input gets a structured
        // 400 rather than a 500, which the server does not implement yet.
        var malformed = Model
            .EnumerateHttpMalformedRequestTests(RestJson1Allowlist.Protocol)
            .ToList();
        var malformedTotal = malformed.Count;
        var execMalformed = malformed.Count(c =>
            GeneratedService.HasHandler(c.OperationName) && RestJson1Allowlist.RunsMalformedCase(c)
        );

        output.WriteLine(
            $"[{RestJson1Allowlist.Protocol}] "
                + $"client-requests: {execClientReq}/{clientReqTotal} ({Pct(execClientReq, clientReqTotal)}), "
                + $"client-responses: {execClientResp}/{clientRespTotal} ({Pct(execClientResp, clientRespTotal)}), "
                + $"server-requests: {execServerReq}/{serverReqTotal} ({Pct(execServerReq, serverReqTotal)}), "
                + $"server-responses: {execServerResp}/{serverRespTotal} ({Pct(execServerResp, serverRespTotal)}), "
                + $"server-malformed: {execMalformed}/{malformedTotal} ({Pct(execMalformed, malformedTotal)})"
        );
    }

    private static string Pct(int part, int whole) =>
        whole == 0 ? "n/a" : $"{(double)part / whole * 100:0.0}%";
}
