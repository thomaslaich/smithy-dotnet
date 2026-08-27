namespace RestXml.Conformance;

/// <summary>
/// Client-applicable <c>httpRequestTests</c> / <c>httpResponseTests</c> case ids for restXml.
/// Every applicable case is executed so additions to the upstream fixtures are covered by default.
/// </summary>
internal static class RestXmlAllowlist
{
    public const string Protocol = "aws.protocols#restXml";

    public static readonly IReadOnlySet<string> ExecutableRequestCases = LoadRequestCases();

    public static readonly IReadOnlySet<string> ExecutableResponseCases = LoadResponseCases();

    private static HashSet<string> LoadRequestCases() =>
        SmithyTestModel
            .Load()
            .EnumerateHttpRequestTests(Protocol)
            .Where(test => test.AppliesToClient)
            .Select(test => test.Id)
            .ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> LoadResponseCases() =>
        SmithyTestModel
            .Load()
            .EnumerateHttpResponseTests(Protocol)
            .Where(test => test.AppliesToClient)
            .Select(test => test.Id)
            .ToHashSet(StringComparer.Ordinal);
}
