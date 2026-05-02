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
        var totalRequests = Model.EnumerateHttpRequestTests(RpcV2CborAllowlist.Protocol).Count();
        var totalResponses = Model.EnumerateHttpResponseTests(RpcV2CborAllowlist.Protocol).Count();
        var execRequests = RpcV2CborAllowlist.ExecutableRequestCases.Count;
        var execResponses = RpcV2CborAllowlist.ExecutableResponseCases.Count;

        output.WriteLine(
            $"[{RpcV2CborAllowlist.Protocol}] requests: {execRequests}/{totalRequests} ({Pct(execRequests, totalRequests)}), responses: {execResponses}/{totalResponses} ({Pct(execResponses, totalResponses)})"
        );
    }

    private static string Pct(int part, int whole) =>
        whole == 0 ? "n/a" : $"{(double)part / whole * 100:0.0}%";
}
