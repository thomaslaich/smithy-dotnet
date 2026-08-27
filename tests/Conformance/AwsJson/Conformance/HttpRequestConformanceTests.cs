namespace AwsJson.Conformance;

public sealed class HttpRequestConformanceTests
{
    private static readonly SmithyTestModel Model = SmithyTestModel.Load();

    public static IEnumerable<object[]> ExecutableCases() =>
        Model
            .EnumerateHttpRequestTests(AwsJsonConformance.Protocol)
            .Where(tc => tc.AppliesToClient)
            .Select(tc => new object[] { tc.Id });

    [Theory]
    [MemberData(nameof(ExecutableCases))]
    public async Task ExecutableHttpRequestCasePassesGeneratedClientConformance(string caseId)
    {
        var testCase = Model
            .EnumerateHttpRequestTests(AwsJsonConformance.Protocol)
            .Single(tc => tc.Id == caseId);
        await HttpRequestRunner.RunAsync(testCase);
    }
}
