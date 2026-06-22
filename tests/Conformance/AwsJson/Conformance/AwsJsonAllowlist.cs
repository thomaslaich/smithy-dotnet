namespace AwsJson.Conformance;

/// <summary>
/// Allowlist of <c>httpRequestTests</c> / <c>httpResponseTests</c> case ids that the generated
/// awsJson client is currently expected to satisfy. Anything not listed here is reported as
/// "unverified" so the executable surface only ever grows on purpose.
/// </summary>
internal static class AwsJsonAllowlist
{
    public const string Protocol = "aws.protocols#awsJson1_1";

    public static readonly IReadOnlySet<string> ExecutableRequestCases = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "AwsJson11SupportsNaNFloatInputs",
        "AwsJson11SupportsInfinityFloatInputs",
        "AwsJson11SupportsNegativeInfinityFloatInputs",
        "includes_x_amz_target_and_content_type",
        "json_1_1_client_sends_empty_payload_for_no_input_shape",
        "sends_requests_to_slash",
    };

    public static readonly IReadOnlySet<string> ExecutableResponseCases = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "AwsJson11SupportsNaNFloatInputs",
        "AwsJson11SupportsInfinityFloatInputs",
        "AwsJson11SupportsNegativeInfinityFloatInputs",
        "handles_empty_output_shape",
        "handles_unexpected_json_output",
        "json_1_1_service_responds_with_no_payload",
        "AwsJson11InvalidGreetingError",
        "AwsJson11ComplexError",
        "AwsJson11EmptyComplexError",
        "AwsJson11FooErrorUsingXAmznErrorType",
        "AwsJson11FooErrorUsingXAmznErrorTypeWithUri",
        "AwsJson11FooErrorUsingXAmznErrorTypeWithUriAndNamespace",
        "AwsJson11FooErrorUsingCode",
        "AwsJson11FooErrorUsingCodeAndNamespace",
        "AwsJson11FooErrorUsingCodeUriAndNamespace",
        "AwsJson11FooErrorWithDunderType",
        "AwsJson11FooErrorWithDunderTypeAndNamespace",
        "AwsJson11FooErrorWithDunderTypeUriAndNamespace",
        "AwsJson11FooErrorWithNestedTypeProperty",
    };
}
