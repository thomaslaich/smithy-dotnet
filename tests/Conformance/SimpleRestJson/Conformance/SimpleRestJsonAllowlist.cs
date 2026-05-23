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
        "SimpleRestJsonNoneHttpPayloadWithDefault",
        "SimpleRestJsonNoneRequiredHttpPayloadWithDefault",
        "PrimitivesEncodingRequest",
        "PreserveKeyOrderRequest",
        "OpenUnionsKnownTaggedUnionCase",
        "OpenUnionsUnknownTaggedUnionCase",
        "OpenUnionsKnownDiscriminatedUnionCase",
        "OpenUnionsUnknownDiscriminatedUnionCase",
        "RoutingAbc",
        "RoutingAbcDef",
        "RoutingAbcDefGreedy",
        "RoutingAbcLabel",
        "RoutingAbcXyz",
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
        "SimpleRestJsonNoneHttpPayloadWithDefault",
        "SimpleRestJsonNoneRequiredHttpPayloadWithDefault",
        "SimpleRestJsonSomeHttpPayloadWithDefault",
        "SimpleRestJsonSomeRequiredHttpPayloadWithDefault",
        "VersionOutput",
        "PrimitivesEncodingResponse",
        "PreserveKeyOrderResponse",
        "OpenUnionsKnownTaggedUnionCase",
        "OpenUnionsUnknownTaggedUnionCase",
        "OpenUnionsKnownDiscriminatedUnionCase",
        "OpenUnionsUnknownDiscriminatedUnionCase",
    };
}
