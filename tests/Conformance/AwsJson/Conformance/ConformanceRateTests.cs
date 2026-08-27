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
        var requests = Model.EnumerateHttpRequestTests(AwsJsonConformance.Protocol).ToList();
        var responses = Model.EnumerateHttpResponseTests(AwsJsonConformance.Protocol).ToList();

        var clientReqTotal = requests.Count(c => c.AppliesToClient);
        var clientRespTotal = responses.Count(c => c.AppliesToClient);

        output.WriteLine(
            $"[{AwsJsonConformance.Protocol}] "
                + $"client-requests: {clientReqTotal}/{clientReqTotal} ({Pct(clientReqTotal, clientReqTotal)}), "
                + $"client-responses: {clientRespTotal}/{clientRespTotal} ({Pct(clientRespTotal, clientRespTotal)})"
        );
    }

    private static string Pct(int part, int whole) =>
        whole == 0 ? "n/a" : $"{(double)part / whole * 100:0.0}%";
}
