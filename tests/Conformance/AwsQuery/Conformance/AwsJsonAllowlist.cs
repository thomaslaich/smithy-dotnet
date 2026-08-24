namespace AwsJson.Conformance;

internal static class AwsJsonAllowlist
{
    public const string Protocol = "aws.protocols#awsQuery";

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
