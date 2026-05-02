namespace RpcV2Cbor.Conformance;

/// <summary>
/// Allowlist of <c>httpRequestTests</c> / <c>httpResponseTests</c> case ids that the generated
/// rpcv2Cbor client is currently expected to satisfy.
/// </summary>
internal static class RpcV2CborAllowlist
{
    public const string Protocol = "smithy.protocols#rpcv2Cbor";

    public static readonly IReadOnlySet<string> ExecutableRequestCases = new HashSet<string>(
        StringComparer.Ordinal
    )
    { };

    public static readonly IReadOnlySet<string> ExecutableResponseCases = new HashSet<string>(
        StringComparer.Ordinal
    )
    { };
}
