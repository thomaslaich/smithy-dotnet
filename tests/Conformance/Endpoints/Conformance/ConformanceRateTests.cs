using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Endpoints.Conformance;

/// <summary>
/// Reports the number of official <c>@endpointTests</c> cases this suite runs, grouped by fixture
/// source. Every case is executable; a failing case fails <see cref="EndpointConformanceTests"/>.
/// </summary>
public sealed class ConformanceRateTests(ITestOutputHelper output)
{
    [Fact]
    public void ReportConformanceRate()
    {
        var model = EndpointTestModel.Current;
        var groups = model
            .ServiceIds.GroupBy(Source)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var cases = group.SelectMany(serviceId => model.TestCases(serviceId)).ToList();
                var operationInputs = cases.Sum(testCase =>
                    testCase!["operationInputs"] is JsonArray inputs ? inputs.Count : 0
                );
                return $"{group.Key}: {cases.Count}/{cases.Count} cases, "
                    + $"{operationInputs}/{operationInputs} operation inputs";
            });

        output.WriteLine($"[endpoints] {string.Join(", ", groups)}");
    }

    // AWS services by their namespace (s3, sts, ...), the vendored fixtures by kind.
    private static string Source(string serviceId)
    {
        const string AwsServices = "com.amazonaws.";
        var ns = serviceId[..serviceId.IndexOf('#', StringComparison.Ordinal)];
        return ns.StartsWith(AwsServices, StringComparison.Ordinal) ? ns[AwsServices.Length..]
            : ns.StartsWith("aws.", StringComparison.Ordinal) ? "aws functions"
            : "rules engine";
    }
}
