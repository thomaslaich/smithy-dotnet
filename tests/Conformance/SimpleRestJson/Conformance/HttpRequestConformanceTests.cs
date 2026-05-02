namespace SimpleRestJson.Conformance;

public sealed class HttpRequestConformanceTests
{
    private static readonly SmithyTestModel Model = SmithyTestModel.Load();

    public static IEnumerable<object[]> ExecutableCases() =>
        Model
            .EnumerateHttpRequestTests(SimpleRestJsonAllowlist.Protocol)
            .Where(tc => SimpleRestJsonAllowlist.ExecutableRequestCases.Contains(tc.Id))
            .Select(tc => new object[] { tc.Id });

    [Theory]
    [MemberData(nameof(ExecutableCases))]
    public async Task ExecutableHttpRequestCasePassesGeneratedClientConformance(string caseId)
    {
        var testCase = Model
            .EnumerateHttpRequestTests(SimpleRestJsonAllowlist.Protocol)
            .Single(tc => tc.Id == caseId);
        await HttpRequestRunner.RunAsync(testCase);
    }

    [Fact]
    public void HttpRequestAllowlistMatchesAvailableCases()
    {
        var available = Model
            .EnumerateHttpRequestTests(SimpleRestJsonAllowlist.Protocol)
            .Select(tc => tc.Id)
            .ToHashSet(StringComparer.Ordinal);
        var missing = SimpleRestJsonAllowlist
            .ExecutableRequestCases.Where(id => !available.Contains(id))
            .ToArray();
        Assert.True(
            missing.Length == 0,
            $"Allowlist references unknown case ids: {string.Join(", ", missing)}"
        );
    }
}
