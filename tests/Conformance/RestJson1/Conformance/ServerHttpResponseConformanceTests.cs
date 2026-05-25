namespace RestJson1.Conformance;

public sealed class ServerHttpResponseConformanceTests
{
    private static readonly SmithyTestModel Model = SmithyTestModel.Load();

    public static IEnumerable<object[]> ExecutableCases() =>
        Model
            .EnumerateHttpResponseTests(RestJson1Allowlist.Protocol)
            .Where(tc => RestJson1Allowlist.ExecutableServerResponseCases.Contains(tc.Id))
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

    [Fact]
    public void HttpResponseServerAllowlistMatchesAvailableCases()
    {
        var available = Model
            .EnumerateHttpResponseTests(RestJson1Allowlist.Protocol)
            .Select(tc => tc.Id)
            .ToHashSet(StringComparer.Ordinal);
        var missing = RestJson1Allowlist
            .ExecutableServerResponseCases.Where(id => !available.Contains(id))
            .ToArray();
        Assert.True(
            missing.Length == 0,
            $"Server allowlist references unknown response case ids: {string.Join(", ", missing)}"
        );
    }
}
