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
        var requests = Model.EnumerateHttpRequestTests(RestXmlAllowlist.Protocol).ToList();
        var responses = Model.EnumerateHttpResponseTests(RestXmlAllowlist.Protocol).ToList();

        var clientRequestTotal = requests.Count(test => test.AppliesToClient);
        var clientResponseTotal = responses.Count(test => test.AppliesToClient);
        var executableClientRequests = requests.Count(test =>
            test.AppliesToClient && RestXmlAllowlist.ExecutableRequestCases.Contains(test.Id)
        );
        var executableClientResponses = responses.Count(test =>
            test.AppliesToClient && RestXmlAllowlist.ExecutableResponseCases.Contains(test.Id)
        );

        output.WriteLine(
            $"[{RestXmlAllowlist.Protocol}] "
                + $"client-requests: {executableClientRequests}/{clientRequestTotal} ({Pct(executableClientRequests, clientRequestTotal)}), "
                + $"client-responses: {executableClientResponses}/{clientResponseTotal} ({Pct(executableClientResponses, clientResponseTotal)})"
        );
    }

    private static string Pct(int part, int whole) =>
        whole == 0 ? "n/a" : $"{(double)part / whole * 100:0.0}%";
}
