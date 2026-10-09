namespace RpcV2Json.Conformance;

public sealed class ServerHttpRequestConformanceTests
{
    private static readonly SmithyTestModel Model = SmithyTestModel.Load();

    public static IEnumerable<object[]> ExecutableCases() =>
        Model
            .EnumerateHttpRequestTests(RpcV2JsonAllowlist.Protocol)
            .Where(tc => RpcV2JsonAllowlist.ExecutableServerRequestCases.Contains(tc.Id))
            .Select(tc => new object[] { tc.Id });

    [Theory]
    [MemberData(nameof(ExecutableCases))]
    public async Task ExecutableHttpRequestCasePassesGeneratedServerConformance(string caseId)
    {
        var testCase = Model
            .EnumerateHttpRequestTests(RpcV2JsonAllowlist.Protocol)
            .Single(tc => tc.Id == caseId);
        await ServerHttpRequestRunner.RunAsync(testCase);
    }

    [Fact]
    public void HttpRequestServerAllowlistMatchesAvailableCases()
    {
        var available = Model
            .EnumerateHttpRequestTests(RpcV2JsonAllowlist.Protocol)
            .Select(tc => tc.Id)
            .ToHashSet(StringComparer.Ordinal);
        var missing = RpcV2JsonAllowlist
            .ExecutableServerRequestCases.Where(id => !available.Contains(id))
            .ToArray();
        Assert.True(
            missing.Length == 0,
            $"Server allowlist references unknown request case ids: {string.Join(", ", missing)}"
        );
    }
}
