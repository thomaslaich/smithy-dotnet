namespace RestJson1.Conformance;

public sealed class ServerHttpRequestConformanceTests
{
    private static readonly SmithyTestModel Model = SmithyTestModel.Load();

    public static IEnumerable<object[]> ExecutableCases() =>
        Model
            .EnumerateHttpRequestTests(RestJson1Allowlist.Protocol)
            .Where(tc => RestJson1Allowlist.ExecutableServerRequestCases.Contains(tc.Id))
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

    [Fact]
    public void HttpRequestServerAllowlistMatchesAvailableCases()
    {
        var available = Model
            .EnumerateHttpRequestTests(RestJson1Allowlist.Protocol)
            .Select(tc => tc.Id)
            .ToHashSet(StringComparer.Ordinal);
        var missing = RestJson1Allowlist
            .ExecutableServerRequestCases.Where(id => !available.Contains(id))
            .ToArray();
        Assert.True(
            missing.Length == 0,
            $"Server allowlist references unknown request case ids: {string.Join(", ", missing)}"
        );
    }
}
