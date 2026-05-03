namespace RestXml.Conformance;

public sealed class HttpRequestConformanceTests
{
    private static readonly SmithyTestModel Model = SmithyTestModel.Load();

    private const string EmptyAllowlistSentinel = "(no executable request cases)";

    public static IEnumerable<object[]> ExecutableCases()
    {
        var ids = Model
            .EnumerateHttpRequestTests(RestXmlAllowlist.Protocol)
            .Where(tc => RestXmlAllowlist.ExecutableRequestCases.Contains(tc.Id))
            .Select(tc => new object[] { tc.Id })
            .ToList();
        if (ids.Count == 0)
            ids.Add(new object[] { EmptyAllowlistSentinel });
        return ids;
    }

    [Theory]
    [MemberData(nameof(ExecutableCases))]
    public async Task ExecutableHttpRequestCasePassesGeneratedClientConformance(string caseId)
    {
        if (caseId == EmptyAllowlistSentinel)
            return;
        var testCase = Model
            .EnumerateHttpRequestTests(RestXmlAllowlist.Protocol)
            .Single(tc => tc.Id == caseId);
        await HttpRequestRunner.RunAsync(testCase);
    }

    [Fact]
    public void HttpRequestAllowlistMatchesAvailableCases()
    {
        var available = Model
            .EnumerateHttpRequestTests(RestXmlAllowlist.Protocol)
            .Select(tc => tc.Id)
            .ToHashSet(StringComparer.Ordinal);
        var missing = RestXmlAllowlist
            .ExecutableRequestCases.Where(id => !available.Contains(id))
            .ToArray();
        Assert.True(
            missing.Length == 0,
            $"Allowlist references unknown case ids: {string.Join(", ", missing)}"
        );
    }
}
