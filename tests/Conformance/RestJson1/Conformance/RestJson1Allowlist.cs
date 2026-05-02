namespace RestJson1.Conformance;

/// <summary>
/// Allowlist of <c>httpRequestTests</c> / <c>httpResponseTests</c> case ids that the generated
/// restJson1 client is currently expected to satisfy. Anything not listed here is reported as
/// "unverified" so the executable surface only ever grows on purpose.
/// </summary>
internal static class RestJson1Allowlist
{
    public const string Protocol = "aws.protocols#restJson1";

    public static readonly IReadOnlySet<string> ExecutableRequestCases = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "HttpQueryParamsOnlyRequest",
        "RestJsonConstantQueryString",
        "RestJsonEmptyInputAndEmptyOutput",
        "RestJsonHttpGetWithNoInput",
        "RestJsonHttpGetWithNoModeledBody",
        "RestJsonHttpPayloadWithStructure",
        "RestJsonHttpPostWithNoInput",
        "RestJsonHttpPostWithNoModeledBody",
        "RestJsonHttpPrefixHeadersArePresent",
        "RestJsonNoInputAndNoOutput",
    };

    public static readonly IReadOnlySet<string> ExecutableResponseCases = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "RestJsonEmptyInputAndEmptyOutput",
        "RestJsonEmptyInputAndEmptyOutputJsonObjectOutput",
        "RestJsonHttpPayloadWithStructure",
        "RestJsonHttpPrefixHeadersArePresent",
        "RestJsonHttpResponseCode",
        "RestJsonHttpResponseCodeWithNoPayload",
    };
}
