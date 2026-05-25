namespace SimpleRestJson.Conformance;

public sealed class HttpRequestConformanceTests
{
    private const string Protocol = "alloy#simpleRestJson";
    private static readonly SmithyTestModel Model = SmithyTestModel.Load();

    public static IEnumerable<object[]> ExecutableCases() =>
        Model.EnumerateHttpRequestTests(Protocol).Select(tc => new object[] { tc.Id });

    [Theory]
    [MemberData(nameof(ExecutableCases))]
    public async Task ExecutableHttpRequestCasePassesGeneratedClientConformance(string caseId)
    {
        var testCase = Model.EnumerateHttpRequestTests(Protocol).Single(tc => tc.Id == caseId);
        await HttpRequestRunner.RunAsync(testCase);
    }
}
