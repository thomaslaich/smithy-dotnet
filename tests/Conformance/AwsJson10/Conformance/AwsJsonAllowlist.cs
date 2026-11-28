namespace AwsJson.Conformance;

internal static class AwsJsonAllowlist
{
    public const string Protocol = "aws.protocols#awsJson1_0";

    /// <summary>
    /// Official cases the client fails today. They stay in the conformance denominator but are
    /// not executed.
    /// </summary>
    public static readonly IReadOnlySet<string> KnownRequestGaps = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        // @awsQueryCompatible services must send `x-amzn-query-mode: true`.
        "QueryCompatibleAwsJson10CborSendsQueryModeHeader",
    };

    public static readonly IReadOnlySet<string> ExecutableRequestCases = LoadRequestCases();

    public static readonly IReadOnlySet<string> ExecutableResponseCases = LoadResponseCases();

    private static HashSet<string> LoadRequestCases() =>
        SmithyTestModel
            .Load()
            .EnumerateHttpRequestTests(Protocol)
            .Where(test => test.AppliesToClient && !KnownRequestGaps.Contains(test.Id))
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
