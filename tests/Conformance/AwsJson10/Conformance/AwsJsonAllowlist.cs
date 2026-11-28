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
    { };

    /// <summary>
    /// Official response cases whose expectation the runner cannot check yet. Their
    /// <c>vendorParams</c> give the awsQuery error code that <c>x-amzn-query-error</c> carries,
    /// and modeled errors do not expose one.
    /// </summary>
    public static readonly IReadOnlySet<string> KnownResponseGaps = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "QueryCompatibleAwsJson10CustomCodeError",
        "QueryCompatibleAwsJson10NoCustomCodeError",
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
            .Where(test => test.AppliesToClient && !KnownResponseGaps.Contains(test.Id))
            .Select(test => test.Id)
            .ToHashSet(StringComparer.Ordinal);
}
