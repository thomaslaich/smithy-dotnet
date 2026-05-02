namespace SimpleRestJson.Conformance;

/// <summary>
/// Initial allowlist of {@code httpRequestTests} cases that we expect the generated
/// simpleRestJson client to satisfy. Each entry is the test trait's {@code id}. Anything not
/// listed here is reported as "unverified" by {@link HttpRequestConformanceTests} so we have a
/// growing-but-known surface, not a silently-shrinking one.
/// </summary>
internal static class SimpleRestJsonAllowlist
{
    public const string Protocol = "alloy#simpleRestJson";

    public static readonly IReadOnlySet<string> ExecutableRequestCases = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "HealthGet",
        "HeaderEndpointInput",
        "CustomCodeInput",
        "GetEnumInput",
        "GetIntEnumInput",
        "RoundTripRequest",
        "AddMenuItem",
        "GetMenuRequest",
        "SimpleRestJsonSomeHttpPayloadWithDefault",
        "SimpleRestJsonSomeRequiredHttpPayloadWithDefault",
        "PrimitivesEncodingRequest",
        "RoutingAbc",
        "RoutingAbcDef",
        "RoutingAbcLabel",
        "RoutingAbcXyz",
        // Known codegen issues (excluded until fixed) — each is a real bug, not a harness gap:
        //   * RoutingAbcDefGreedy — greedy label URI expansion is not implemented.
        //   * SimpleRestJsonNoneHttpPayloadWithDefault*  — default-valued payloads are not omitted.
        //   * PreserveKeyOrderRequest — Document type binding/serialization not validated yet.
        // OpenUnions* require open-union codegen support which we haven’t validated yet.
    };

    public static readonly IReadOnlySet<string> ExecutableResponseCases = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "AddMenuItemResult",
        "CustomCodeOutput",
        "GetEnumOutput",
        "GetIntEnumOutput",
        "GetMenuResponse",
        "headerEndpointResponse",
        "NotFoundError",
        "PriceErrorTest",
        "RoundTripDataResponse",
        "SimpleRestJsonSomeHttpPayloadWithDefault",
        "SimpleRestJsonSomeRequiredHttpPayloadWithDefault",
        "VersionOutput",
        // Known codegen issues (excluded until fixed):
        //   * SimpleRestJsonNone*HttpPayloadWithDefault* — default-payload omission.
        //   * PrimitivesEncodingResponse — covered by request side; response not yet handled.
        //   * PreserveKeyOrderResponse — Document support pending.
        // OpenUnions* require open-union codegen support.
    };
}
