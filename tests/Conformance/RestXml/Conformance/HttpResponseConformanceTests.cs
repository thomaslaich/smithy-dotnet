using System.Text.Json.Nodes;

namespace RestXml.Conformance;

public sealed class HttpResponseConformanceTests
{
    private static readonly SmithyTestModel Model = SmithyTestModel.Load();

    public static IEnumerable<object[]> ExecutableCases() =>
        Model
            .EnumerateHttpResponseTests(RestXmlAllowlist.Protocol)
            .Where(tc => RestXmlAllowlist.ExecutableResponseCases.Contains(tc.Id))
            .Select(tc => new object[] { tc.Id });

    [Theory]
    [MemberData(nameof(ExecutableCases))]
    public async Task ExecutableHttpResponseCasePassesGeneratedClientConformance(string caseId)
    {
        var testCase = Model
            .EnumerateHttpResponseTests(RestXmlAllowlist.Protocol)
            .Single(tc => tc.Id == caseId);
        await HttpResponseRunner.RunAsync(testCase, Model.RawShapes);
    }

    [Fact]
    public void HttpResponseAllowlistMatchesAvailableCases()
    {
        var available = Model
            .EnumerateHttpResponseTests(RestXmlAllowlist.Protocol)
            .Select(tc => tc.Id)
            .ToHashSet(StringComparer.Ordinal);
        var missing = RestXmlAllowlist
            .ExecutableResponseCases.Where(id => !available.Contains(id))
            .ToArray();
        Assert.True(
            missing.Length == 0,
            $"Allowlist references unknown response case ids: {string.Join(", ", missing)}"
        );
    }
}
