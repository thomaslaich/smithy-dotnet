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
        "empty_input",
        "no_input",
        "optional_input",
        "RpcV2CborClientSkipsTopLevelDefaultValuesInInput",
        "RpcV2CborClientUsesExplicitlyProvidedValuesInTopLevel",
        "RpcV2CborClientIgnoresNonTopLevelDefaultsOnMembersWithClientOptional",
        "RpcV2CborClientDoesntSerializeNullStructureValues",
        "RpcV2CborLists",
        "RpcV2CborListsEmpty",
        "RpcV2CborMaps",
        "RpcV2CborSerializesSparseSetMap",
        "RpcV2CborSerializesDenseSetMap",
        "RpcV2CborSerializesZeroValuesInMaps",
        "RpcV2CborSparseListsSerializeNull",
        "RpcV2CborSerializesNullMapValues",
        "RpcV2CborSerializesZeroValuesInSparseMaps",
        "RpcV2CborSerializesSparseSetMapAndRetainsNull",
        "RpcV2CborSparseMaps",
        "RpcV2CborRecursiveShapes",
        "RpcV2CborSupportsNaNFloatInputs",
        "RpcV2CborSupportsInfinityFloatInputs",
        "RpcV2CborSupportsNegativeInfinityFloatInputs",
        "RpcV2CborSimpleScalarProperties",
    };

    public static readonly IReadOnlySet<string> ExecutableResponseCases = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "empty_output",
        "no_output",
        "optional_output",
        "RpcV2CborSimpleScalarProperties",
        "RpcV2CborClientPopulatesDefaultsValuesWhenMissingInResponse",
        "RpcV2CborClientIgnoresDefaultValuesIfMemberValuesArePresentInResponse",
        "RpcV2CborClientDoesntDeserializeNullStructureValues",
        "RpcV2CborLists",
        "RpcV2CborListsEmpty",
        "RpcV2CborMaps",
        "RpcV2CborSparseMapsDeserializeNullValues",
        "RpcV2CborSparseJsonMaps",
        "RpcV2CborDeserializesDenseSetMap",
        "RpcV2CborDeserializesSparseSetMap",
        "RpcV2CborDeserializesZeroValuesInMaps",
        "RpcV2CborDeserializesNullMapValues",
        "RpcV2CborDeserializesZeroValuesInSparseMaps",
        "RpcV2CborDeserializesSparseSetMapAndRetainsNull",
        "RpcV2CborSparseListsDeserializeNull",
        "RpcV2CborRecursiveShapes",
        "RpcV2CborSupportsNaNFloatOutputs",
        "RpcV2CborSupportsInfinityFloatOutputs",
        "RpcV2CborSupportsNegativeInfinityFloatOutputs",
        "RpcV2CborServerDoesntSerializeNullStructureValues",
    };

    public static readonly IReadOnlySet<string> ExecutableServerRequestCases = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "empty_input",
        "no_input",
        "optional_input",
        "RpcV2CborSimpleScalarProperties",
        "RpcV2CborServerPopulatesDefaultsWhenMissingInRequestBody",
        "RpcV2CborServerDoesntDeSerializeNullStructureValues",
        "RpcV2CborLists",
        "RpcV2CborListsEmpty",
        "RpcV2CborMaps",
        "RpcV2CborRecursiveShapes",
    };

    public static readonly IReadOnlySet<string> ExecutableServerResponseCases =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "empty_output",
            "no_output",
            "RpcV2CborSimpleScalarProperties",
            "RpcV2CborServerDoesntSerializeNullStructureValues",
        };
}
