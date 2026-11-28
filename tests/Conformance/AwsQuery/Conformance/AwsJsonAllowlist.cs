namespace AwsJson.Conformance;

internal static class AwsJsonAllowlist
{
    public const string Protocol = "aws.protocols#awsQuery";

    /// <summary>
    /// Official cases that stay in the conformance denominator but do not run, because their
    /// expectation cannot be checked yet.
    /// </summary>
    public static readonly IReadOnlySet<string> KnownResponseGaps = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        // Their vendorParams give the error code, which modeled errors do not expose.
        "QueryComplexError",
        "QueryCustomizedError",
        "QueryInvalidGreetingError",
    };

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
            .Where(test => test.AppliesToClient && !KnownResponseGaps.Contains(test.Id))
            .Select(test => test.Id)
            .ToHashSet(StringComparer.Ordinal);
}
