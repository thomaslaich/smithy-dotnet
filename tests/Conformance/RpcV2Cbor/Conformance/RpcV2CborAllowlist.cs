using RpcV2.Conformance;

namespace RpcV2Cbor.Conformance;

/// <summary>
/// Every official rpcv2Cbor case applicable to a side runs, except the known gaps listed here.
/// A gap stays in the conformance denominator but not the numerator.
/// </summary>
internal static class RpcV2CborAllowlist
{
    public const string Protocol = "smithy.protocols#rpcv2Cbor";

    /// <summary>Client-request cases the generated client fails today.</summary>
    public static readonly IReadOnlySet<string> KnownRequestGaps = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        // @awsQueryCompatible clients must send `x-amzn-query-mode: true`.
        "QueryCompatibleRpcV2CborSendsQueryModeHeader",
    };

    /// <summary>Client-response cases the generated client fails today.</summary>
    public static readonly IReadOnlySet<string> KnownResponseGaps = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        // Their vendorParams give the error code, which modeled errors do not expose.
        "QueryCompatibleRpcV2CborCustomCodeError",
        "QueryCompatibleRpcV2CborNoCustomCodeError",
    };

    /// <summary>Server-request cases the generated server fails today.</summary>
    public static readonly IReadOnlySet<string> KnownServerRequestGaps = new HashSet<string>(
        StringComparer.Ordinal
    )
    { };

    /// <summary>Server-response cases the generated server fails today.</summary>
    public static readonly IReadOnlySet<string> KnownServerResponseGaps = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        // @awsQueryCompatible servers must send the error's awsQuery code in `x-amzn-query-error`.
        "QueryCompatibleRpcV2CborCustomCodeError",
    };

    public static readonly IReadOnlySet<string> ExecutableRequestCases = Load(
        requests: true,
        client: true,
        KnownRequestGaps
    );

    public static readonly IReadOnlySet<string> ExecutableResponseCases = Load(
        requests: false,
        client: true,
        KnownResponseGaps
    );

    public static readonly IReadOnlySet<string> ExecutableServerRequestCases = Load(
        requests: true,
        client: false,
        KnownServerRequestGaps
    );

    public static readonly IReadOnlySet<string> ExecutableServerResponseCases = Load(
        requests: false,
        client: false,
        KnownServerResponseGaps
    );

    private static HashSet<string> Load(bool requests, bool client, IReadOnlySet<string> gaps)
    {
        var model = SmithyTestModel.Load();
        var cases = requests
            ? model
                .EnumerateHttpRequestTests(Protocol)
                .Select(c => (c.Id, c.AppliesToClient, c.AppliesToServer))
            : model
                .EnumerateHttpResponseTests(Protocol)
                .Select(c => (c.Id, c.AppliesToClient, c.AppliesToServer));
        return cases
            .Where(c => (client ? c.AppliesToClient : c.AppliesToServer) && !gaps.Contains(c.Id))
            .Select(c => c.Id)
            .ToHashSet(StringComparer.Ordinal);
    }
}
