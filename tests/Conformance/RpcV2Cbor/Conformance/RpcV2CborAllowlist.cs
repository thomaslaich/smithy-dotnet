namespace RpcV2Cbor.Conformance;

/// <summary>
/// Allowlist of <c>httpRequestTests</c> / <c>httpResponseTests</c> case ids that the generated
/// rpcv2Cbor client and server are currently expected to satisfy.
/// </summary>
internal static class RpcV2CborAllowlist
{
    public const string Protocol = "smithy.protocols#rpcv2Cbor";

    public static readonly IReadOnlySet<string> ExecutableRequestCases = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "RpcV2CborClientDoesntSerializeNullStructureValues",
        "RpcV2CborClientIgnoresNonTopLevelDefaultsOnMembersWithClientOptional",
        "RpcV2CborClientPopulatesDefaultValuesInInput",
        "RpcV2CborClientSkipsTopLevelDefaultValuesInInput",
        "RpcV2CborClientUsesExplicitlyProvidedMemberValuesOverDefaults",
        "RpcV2CborClientUsesExplicitlyProvidedValuesInTopLevel",
        "RpcV2CborLists",
        "RpcV2CborListsEmpty",
        "RpcV2CborListsEmptyUsingDefiniteLength",
        "RpcV2CborMaps",
        "RpcV2CborRecursiveShapes",
        "RpcV2CborSerializesDenseSetMap",
        "RpcV2CborSerializesNullMapValues",
        "RpcV2CborSerializesSparseSetMap",
        "RpcV2CborSerializesSparseSetMapAndRetainsNull",
        "RpcV2CborSerializesZeroValuesInMaps",
        "RpcV2CborSerializesZeroValuesInSparseMaps",
        "RpcV2CborSimpleScalarProperties",
        "RpcV2CborSparseListsSerializeNull",
        "RpcV2CborSparseMaps",
        "RpcV2CborSparseMapsSerializeNullValues",
        "RpcV2CborSupportsInfinityFloatInputs",
        "RpcV2CborSupportsNaNFloatInputs",
        "RpcV2CborSupportsNegativeInfinityFloatInputs",
        "empty_input",
        "no_input",
        "optional_input",
    };

    public static readonly IReadOnlySet<string> ExecutableResponseCases = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "NoOutputClientAllowsEmptyBody",
        "NoOutputClientAllowsEmptyCbor",
        "RpcV2CborClientDoesntDeserializeNullStructureValues",
        "RpcV2CborClientIgnoresDefaultValuesIfMemberValuesArePresentInResponse",
        "RpcV2CborClientPopulatesDefaultsValuesWhenMissingInResponse",
        "RpcV2CborComplexError",
        "RpcV2CborDateTimeWithFractionalSeconds",
        "RpcV2CborDeserializesDenseSetMap",
        "RpcV2CborDeserializesNullMapValues",
        "RpcV2CborDeserializesSparseSetMap",
        "RpcV2CborDeserializesSparseSetMapAndRetainsNull",
        "RpcV2CborDeserializesZeroValuesInMaps",
        "RpcV2CborDeserializesZeroValuesInSparseMaps",
        "RpcV2CborEmptyComplexError",
        "RpcV2CborExtraFieldsInTheBodyShouldBeSkippedByClients",
        "RpcV2CborFloat16Inf",
        "RpcV2CborFloat16LSBNaN",
        "RpcV2CborFloat16MSBNaN",
        "RpcV2CborFloat16NegInf",
        "RpcV2CborFloat16Subnormal",
        "RpcV2CborIndefiniteStringInsideDefiniteListCanDeserialize",
        "RpcV2CborIndefiniteStringInsideIndefiniteListCanDeserialize",
        "RpcV2CborInvalidGreetingError",
        "RpcV2CborLists",
        "RpcV2CborListsEmpty",
        "RpcV2CborMaps",
        "RpcV2CborRecursiveShapes",
        "RpcV2CborRecursiveShapesUsingDefiniteLength",
        "RpcV2CborSimpleScalarProperties",
        "RpcV2CborSimpleScalarPropertiesUsingDefiniteLength",
        "RpcV2CborSparseJsonMaps",
        "RpcV2CborSparseListsDeserializeNull",
        "RpcV2CborSparseMapsDeserializeNullValues",
        "RpcV2CborSupportsInfinityFloatOutputs",
        "RpcV2CborSupportsNaNFloatOutputs",
        "RpcV2CborSupportsNegativeInfinityFloatOutputs",
        "RpcV2CborSupportsUpcastingDataOnDeserialize",
        "empty_output",
        "empty_output_no_body",
        "no_output",
        "optional_output",
    };

    public static readonly IReadOnlySet<string> ExecutableServerRequestCases = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "NoInputServerAllowsEmptyBody",
        "NoInputServerAllowsEmptyCbor",
        "RpcV2CborExtraFieldsInTheBodyShouldBeSkippedByServers",
        "RpcV2CborIndefiniteLengthByteStringsCanBeDeserialized",
        "RpcV2CborIndefiniteLengthStringsCanBeDeserialized",
        "RpcV2CborIndefiniteStringInsideDefiniteList",
        "RpcV2CborIndefiniteStringInsideIndefiniteList",
        "RpcV2CborLists",
        "RpcV2CborListsEmpty",
        "RpcV2CborListsEmptyUsingDefiniteLength",
        "RpcV2CborMaps",
        "RpcV2CborRecursiveShapes",
        "RpcV2CborSerializesDenseSetMap",
        "RpcV2CborSerializesNullMapValues",
        "RpcV2CborSerializesSparseSetMap",
        "RpcV2CborSerializesSparseSetMapAndRetainsNull",
        "RpcV2CborSerializesZeroValuesInMaps",
        "RpcV2CborSerializesZeroValuesInSparseMaps",
        "RpcV2CborServerDoesntDeSerializeNullStructureValues",
        "RpcV2CborServerPopulatesDefaultsWhenMissingInRequestBody",
        "RpcV2CborServersShouldHandleNoAcceptHeader",
        "RpcV2CborSimpleScalarProperties",
        "RpcV2CborSimpleScalarPropertiesUsingIndefiniteLength",
        "RpcV2CborSparseListsSerializeNull",
        "RpcV2CborSparseMaps",
        "RpcV2CborSparseMapsSerializeNullValues",
        "RpcV2CborSupportsInfinityFloatInputs",
        "RpcV2CborSupportsNaNFloatInputs",
        "RpcV2CborSupportsNegativeInfinityFloatInputs",
        "RpcV2CborSupportsUpcastingData",
        "empty_input",
        "empty_input_no_body",
        "empty_input_no_body_has_accept",
        "no_input",
        "optional_input",
    };

    public static readonly IReadOnlySet<string> ExecutableServerResponseCases = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "RpcV2CborComplexError",
        "RpcV2CborDeserializesDenseSetMap",
        "RpcV2CborDeserializesNullMapValues",
        "RpcV2CborDeserializesSparseSetMap",
        "RpcV2CborDeserializesSparseSetMapAndRetainsNull",
        "RpcV2CborDeserializesZeroValuesInMaps",
        "RpcV2CborDeserializesZeroValuesInSparseMaps",
        "RpcV2CborEmptyComplexError",
        "RpcV2CborInvalidGreetingError",
        "RpcV2CborLists",
        "RpcV2CborListsEmpty",
        "RpcV2CborMaps",
        "RpcV2CborRecursiveShapes",
        "RpcV2CborServerDoesntSerializeNullStructureValues",
        "RpcV2CborServerPopulatesDefaultsInResponseWhenMissingInParams",
        "RpcV2CborSimpleScalarProperties",
        "RpcV2CborSparseJsonMaps",
        "RpcV2CborSparseListsDeserializeNull",
        "RpcV2CborSparseMapsDeserializeNullValues",
        "RpcV2CborSupportsInfinityFloatOutputs",
        "RpcV2CborSupportsNaNFloatOutputs",
        "RpcV2CborSupportsNegativeInfinityFloatOutputs",
        "empty_output",
        "no_output",
        "optional_output",
    };
}
