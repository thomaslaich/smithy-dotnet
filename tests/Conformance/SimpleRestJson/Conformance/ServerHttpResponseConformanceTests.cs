namespace SimpleRestJson.Conformance;

public sealed class ServerHttpResponseConformanceTests
{
    private const string Protocol = "alloy#simpleRestJson";
    private static readonly SmithyTestModel Model = SmithyTestModel.Load();

    public static IEnumerable<object[]> ExecutableCases() =>
        Model
            .EnumerateHttpResponseTests(Protocol)
            .Where(tc => tc.AppliesToServer)
            .Select(tc => new object[] { tc.Id });

    [Theory]
    [MemberData(nameof(ExecutableCases))]
    public async Task ExecutableHttpResponseCasePassesGeneratedServerConformance(string caseId)
    {
        var testCase = Model.EnumerateHttpResponseTests(Protocol).Single(tc => tc.Id == caseId);
        await ServerHttpResponseRunner.RunAsync(testCase, Model.RawShapes);
    }
}
