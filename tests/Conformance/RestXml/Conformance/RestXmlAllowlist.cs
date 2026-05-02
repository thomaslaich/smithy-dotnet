namespace RestXml.Conformance;

/// <summary>
/// Allowlist of <c>httpRequestTests</c> / <c>httpResponseTests</c> case ids that the generated
/// restXml client is currently expected to satisfy. Empty until cases are individually verified.
/// </summary>
internal static class RestXmlAllowlist
{
    public const string Protocol = "aws.protocols#restXml";

    public static readonly IReadOnlySet<string> ExecutableRequestCases = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
    };

    public static readonly IReadOnlySet<string> ExecutableResponseCases = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
    };
}
