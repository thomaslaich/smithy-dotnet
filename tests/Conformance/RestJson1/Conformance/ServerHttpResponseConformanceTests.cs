namespace RestJson1.Conformance;

public sealed class ServerHttpResponseConformanceTests
{
    private static readonly SmithyTestModel Model = SmithyTestModel.Load();

    public static IEnumerable<object[]> ExecutableCases() =>
        Model
            .EnumerateHttpResponseTests(RestJson1Allowlist.Protocol)
            .Where(tc => tc.AppliesToServer)
            .Select(tc => new object[] { tc.Id });

    [Theory]
    [MemberData(nameof(ExecutableCases))]
    public async Task ExecutableHttpResponseCasePassesGeneratedServerConformance(string caseId)
    {
        var testCase = Model
            .EnumerateHttpResponseTests(RestJson1Allowlist.Protocol)
            .Single(tc => tc.Id == caseId);
        await ServerHttpResponseRunner.RunAsync(testCase, Model.RawShapes);
    }
}
