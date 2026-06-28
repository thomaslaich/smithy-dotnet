namespace RestJson1.Conformance;

public sealed class ServerHttpRequestConformanceTests
{
    private static readonly SmithyTestModel Model = SmithyTestModel.Load();

    public static IEnumerable<object[]> ExecutableCases() =>
        Model
            .EnumerateHttpRequestTests(RestJson1Allowlist.Protocol)
            .Where(tc => tc.AppliesToServer && GeneratedService.HasHandler(tc.OperationName))
            .Select(tc => new object[] { tc.Id });

    [Theory]
    [MemberData(nameof(ExecutableCases))]
    public async Task ExecutableHttpRequestCasePassesGeneratedServerConformance(string caseId)
    {
        var testCase = Model
            .EnumerateHttpRequestTests(RestJson1Allowlist.Protocol)
            .Single(tc => tc.Id == caseId);
        await ServerHttpRequestRunner.RunAsync(testCase);
    }
}
