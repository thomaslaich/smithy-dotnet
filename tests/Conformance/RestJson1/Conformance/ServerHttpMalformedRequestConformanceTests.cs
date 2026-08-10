namespace RestJson1.Conformance;

public sealed class ServerHttpMalformedRequestConformanceTests
{
    private static readonly SmithyTestModel Model = SmithyTestModel.Load();

    public static IEnumerable<object[]> ExecutableCases() =>
        Model
            .EnumerateHttpMalformedRequestTests(RestJson1Allowlist.Protocol)
            .Where(tc => GeneratedService.HasHandler(tc.OperationName))
            .Where(RestJson1Allowlist.RunsMalformedCase)
            .Select(tc => new object[] { tc.Id });

    [Theory]
    [MemberData(nameof(ExecutableCases))]
    public async Task ExecutableMalformedRequestCasePassesGeneratedServerConformance(string caseId)
    {
        var testCase = Model
            .EnumerateHttpMalformedRequestTests(RestJson1Allowlist.Protocol)
            .Single(tc => tc.Id == caseId);
        await ServerHttpMalformedRequestRunner.RunAsync(testCase);
    }
}
