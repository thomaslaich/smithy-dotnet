using System.Text.Json.Nodes;

namespace AwsJson.Conformance;

public sealed class HttpResponseConformanceTests
{
    private static readonly SmithyTestModel Model = SmithyTestModel.Load();

    public static IEnumerable<object[]> ExecutableCases() =>
        Model
            .EnumerateHttpResponseTests(AwsJsonConformance.Protocol)
            .Where(tc => tc.AppliesToClient)
            .Select(tc => new object[] { tc.Id });

    [Theory]
    [MemberData(nameof(ExecutableCases))]
    public async Task ExecutableHttpResponseCasePassesGeneratedClientConformance(string caseId)
    {
        var testCase = Model
            .EnumerateHttpResponseTests(AwsJsonConformance.Protocol)
            .Single(tc => tc.Id == caseId);
        await HttpResponseRunner.RunAsync(testCase, Model.RawShapes);
    }
}
