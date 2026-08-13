namespace SimpleRestJson.Conformance;

public sealed class ServerHttpRequestConformanceTests
{
    private const string Protocol = "alloy#simpleRestJson";
    private static readonly SmithyTestModel Model = SmithyTestModel.Load();

    public static IEnumerable<object[]> ExecutableCases() =>
        Model
            .EnumerateHttpRequestTests(Protocol)
            .Where(tc => tc.AppliesToServer && GeneratedService.HasHandler(tc.OperationName))
            .Where(tc => !KnownParamGaps.ServerRequest.Contains(tc.Id))
            .Select(tc => new object[] { tc.Id });

    [Theory]
    [MemberData(nameof(ExecutableCases))]
    public async Task ExecutableHttpRequestCasePassesGeneratedServerConformance(string caseId)
    {
        var testCase = Model.EnumerateHttpRequestTests(Protocol).Single(tc => tc.Id == caseId);
        await ServerHttpRequestRunner.RunAsync(testCase);
    }
}
