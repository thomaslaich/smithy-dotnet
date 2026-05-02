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
        // PreserveKeyOrderRequest takes a Document map and exercises insertion-order
        // semantics — the binder doesn’t support Document yet.
        // Routing* cases live on a separate test service, not on PizzaAdminService.
        // OpenUnions* require open-union codegen support which we haven't validated yet.
        // SimpleRestJsonNoneHttpPayloadWithDefault* expect default-valued payloads to be
        // omitted from the wire — current codegen always serializes them. Tracked separately.
    };
}
