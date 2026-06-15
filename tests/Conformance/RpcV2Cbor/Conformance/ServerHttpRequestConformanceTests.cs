namespace RpcV2Cbor.Conformance;

public sealed class ServerHttpRequestConformanceTests
{
    private static readonly SmithyTestModel Model = SmithyTestModel.Load();

    public static IEnumerable<object[]> ExecutableCases() =>
        Model
            .EnumerateHttpRequestTests(RpcV2CborAllowlist.Protocol)
            .Where(tc => RpcV2CborAllowlist.ExecutableServerRequestCases.Contains(tc.Id))
            .Select(tc => new object[] { tc.Id });

    [Theory]
    [MemberData(nameof(ExecutableCases))]
    public async Task ExecutableHttpRequestCasePassesGeneratedServerConformance(string caseId)
    {
        var testCase = Model
            .EnumerateHttpRequestTests(RpcV2CborAllowlist.Protocol)
            .Single(tc => tc.Id == caseId);
        await ServerHttpRequestRunner.RunAsync(testCase);
    }

    [Fact]
    public void HttpRequestServerAllowlistMatchesAvailableCases()
    {
        var available = Model
            .EnumerateHttpRequestTests(RpcV2CborAllowlist.Protocol)
            .Select(tc => tc.Id)
            .ToHashSet(StringComparer.Ordinal);
        var missing = RpcV2CborAllowlist
            .ExecutableServerRequestCases.Where(id => !available.Contains(id))
            .ToArray();
        Assert.True(
            missing.Length == 0,
            $"Server allowlist references unknown request case ids: {string.Join(", ", missing)}"
        );
    }
}
