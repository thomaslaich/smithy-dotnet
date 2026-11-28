namespace RestXml.Conformance;

/// <summary>
/// Client-applicable <c>httpRequestTests</c> / <c>httpResponseTests</c> case ids for restXml.
/// Every applicable case is executed so additions to the upstream fixtures are covered by default.
/// </summary>
internal static class RestXmlAllowlist
{
    public const string Protocol = "aws.protocols#restXml";

    /// <summary>
    /// Official cases that stay in the conformance denominator but do not run, because their
    /// expectation cannot be checked yet.
    /// </summary>
    public static readonly IReadOnlySet<string> KnownRequestGaps = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        // S3 cases whose scopedConfig (region, addressing style, dual-stack, accelerate) the runner does not apply.
        "S3DefaultAddressing",
        "S3EscapeObjectKeyInUriLabel",
        "S3EscapePathObjectKeyInUriLabel",
        "S3OperationAddressingPreferred",
        "S3PreservesEmbeddedDotSegmentInUriLabel",
        "S3PreservesLeadingDotSegmentInUriLabel",
        "S3VirtualHostAccelerateAddressing",
        "S3VirtualHostAddressing",
        "S3VirtualHostDualstackAccelerateAddressing",
        "S3VirtualHostDualstackAddressing",
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
