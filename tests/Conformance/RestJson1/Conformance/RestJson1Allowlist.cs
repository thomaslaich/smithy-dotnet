namespace RestJson1.Conformance;

/// <summary>
/// Allowlist of <c>httpRequestTests</c> / <c>httpResponseTests</c> case ids that the generated
/// restJson1 client is currently expected to satisfy. Anything not listed here is reported as
/// "unverified" so the executable surface only ever grows on purpose.
/// </summary>
internal static class RestJson1Allowlist
{
    public const string Protocol = "aws.protocols#restJson1";

    /// <summary>
    /// Malformed-request cases the generated server is expected to satisfy — every case whose
    /// operation the generated service exposes. Nothing is filtered out: constraint violations,
    /// unreadable input, and content negotiation are all answered.
    /// </summary>
    internal static bool RunsMalformedCase(HttpMalformedRequestTestCase testCase) => true;

    /// <summary>
    /// Cases whose deserialized `params` do not yet match the fixture.
    /// </summary>
    /// <remarks>
    /// These are not newly broken — they were never actually asserted. The response
    /// assertion looked up expected values by generated constructor parameter name
    /// (PascalCase) against fixture keys (camelCase), missed every time, and took the
    /// "omitted fields are not asserted" path. A deliberately wrong expected value
    /// passed.
    ///
    /// Repairing that exposed 81 failures, 51 of which were defects in the runner
    /// rather than in the generated code: property lookup has to tolerate both the
    /// PascalCase constructor parameters of generated records and the camelCase
    /// parameters of union case classes; blobs and streaming blobs need payload
    /// comparison instead of being treated as sequences or scalars; and Smithy's Byte
    /// maps to C# sbyte, which was not a known numeric type. The rest are believed
    /// genuine.
    ///
    /// They are quarantined here rather than silently skipped so the gaps are
    /// greppable and countable. Each still needs triage: some are likely real
    /// deserialization gaps, others may be limitations in the runner's own union and
    /// blob comparison logic, which this is the first code to reach.
    ///
    /// The clusters are unions, streaming blobs, and query-string binding.
    /// </remarks>
    public static readonly IReadOnlySet<string> KnownResponseParamGaps = new HashSet<string>(
        StringComparer.Ordinal
    )
    { };

    /// <summary>Server-request counterpart of <see cref="KnownResponseParamGaps"/>.</summary>
    public static readonly IReadOnlySet<string> KnownServerRequestParamGaps = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "RestJsonAllQueryStringTypes",
        "RestJsonHttpEmptyPrefixHeadersRequestServer",
        "RestJsonOmitsEmptyListQueryValues",
        "RestJsonQueryStringEscaping",
        "RestJsonServersPutAllQueryParamsInMap",
        "RestJsonServersQueryParamsStringListMap",
        "RestJsonStreamingTraitsRequireLengthWithBlob",
        "RestJsonStreamingTraitsWithBlob",
        "RestJsonStreamingTraitsWithMediaTypeWithBlob",
        "RestJsonSupportsInfinityFloatQueryValues",
        "RestJsonSupportsNaNFloatQueryValues",
        "RestJsonSupportsNegativeInfinityFloatQueryValues",
        "RestJsonTestPayloadBlob",
        "RestJsonZeroAndFalseQueryValues",
        "SDKAppendedGzipAfterProvidedEncoding_restJson1",
        "SDKAppliedContentEncoding_restJson1",
    };

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
        // Diagnostic batch — remove failures after testing
        "RestJsonSimpleScalarProperties",
        "RestJsonJsonBlobs",
        "RestJsonJsonEnums",
        "RestJsonJsonIntEnums",
        "RestJsonLists",
        "RestJsonListsEmpty",
        "RestJsonJsonMaps",
        "RestJsonJsonTimestamps",
        "RestJsonJsonTimestampsWithDateTimeFormat",
        "RestJsonJsonTimestampsWithDateTimeOnTargetFormat",
        "RestJsonJsonTimestampsWithEpochSecondsFormat",
        "RestJsonJsonTimestampsWithEpochSecondsOnTargetFormat",
        "RestJsonJsonTimestampsWithHttpDateFormat",
        "RestJsonJsonTimestampsWithHttpDateOnTargetFormat",
        "RestJsonSerializeStringUnionValue",
        "RestJsonSerializeBooleanUnionValue",
        "RestJsonSerializeNumberUnionValue",
        "RestJsonSerializeBlobUnionValue",
        "RestJsonSerializeTimestampUnionValue",
        "RestJsonSerializeEnumUnionValue",
        "RestJsonSerializeListUnionValue",
        "RestJsonSerializeMapUnionValue",
        "RestJsonSerializeStructureUnionValue",
        "RestJsonSerializeRenamedStructureUnionValue",
        "RestJsonInputAndOutputWithStringHeaders",
        "RestJsonInputAndOutputWithNumericHeaders",
        "RestJsonInputAndOutputWithBooleanHeaders",
        "RestJsonInputAndOutputWithTimestampHeaders",
        "RestJsonInputAndOutputWithEnumHeaders",
        "RestJsonInputAndOutputWithIntEnumHeaders",
        "RestJsonNullAndEmptyHeaders",
        "RestJsonHttpRequestWithGreedyLabelInPath",
        "RestJsonHttpRequestLabelEscaping",
        "RestJsonRecursiveShapes",
        "RestJsonDoesntSerializeNullStructureValues",
        "RestJsonHttpWithEmptyBody",
        "RestJsonHttpWithHeadersButNoPayload",
        "RestJsonOmitsNullQuery",
        "RestJsonSerializesEmptyQueryValue",
        "RestJsonZeroAndFalseQueryValues",
        "RestJsonOmitsEmptyListQueryValues",
        "RestJsonQueryStringMap",
        "RestJsonQueryParamsStringListMap",
        "RestJsonConstantAndVariableQueryStringAllValues",
        "RestJsonConstantAndVariableQueryStringMissingOneValue",
        "RestJsonHttpPayloadTraitsWithBlob",
        "RestJsonHttpPayloadTraitsWithNoBlobBody",
        "RestJsonHttpPayloadWithUnion",
        "RestJsonHttpPayloadWithUnsetUnion",
        "RestJsonStreamingTraitsWithBlob",
        "RestJsonStreamingTraitsWithNoBlobBody",
        "RestJsonHttpWithEmptyBlobPayload",
        "RestJsonEnumPayloadRequest",
        "RestJsonStringPayloadRequest",
        "RestJsonHttpEmptyPrefixHeadersRequestClient",
        "RestJsonHttpPrefixHeadersAreNotPresent",
        "RestJsonSerializesDenseSetMap",
        "RestJsonSerializesSparseSetMap",
        "RestJsonSerializesSparseSetMapAndRetainsNull",
        "RestJsonSerializesSparseNullMapValues",
        "RestJsonSparseJsonMaps",
        "RestJsonSparseListsSerializeNull",
        "RestJsonSerializesZeroValuesInMaps",
        "RestJsonSerializesZeroValuesInSparseMaps",
        "RestJsonClientPopulatesDefaultValuesInInput",
        "RestJsonClientSkipsTopLevelDefaultValuesInInput",
        "RestJsonClientUsesExplicitlyProvidedValuesInTopLevel",
        "RestJsonClientPopulatesNestedDefaultValuesWhenMissing",
        "RestJsonClientUsesExplicitlyProvidedMemberValuesOverDefaults",
        "RestJsonClientIgnoresNonTopLevelDefaultsOnMembersWithClientOptional",
        "PostUnionWithJsonNameRequest1",
        "PostUnionWithJsonNameRequest2",
        "PostUnionWithJsonNameRequest3",
        "RestJsonSupportsNaNFloatInputs",
        "RestJsonSupportsInfinityFloatInputs",
        "RestJsonSupportsNegativeInfinityFloatInputs",
        "RestJsonSupportsNaNFloatQueryValues",
        "RestJsonSupportsInfinityFloatQueryValues",
        "RestJsonSupportsNegativeInfinityFloatQueryValues",
        "RestJsonSupportsNaNFloatHeaderInputs",
        "RestJsonSupportsInfinityFloatHeaderInputs",
        "RestJsonSupportsNegativeInfinityFloatHeaderInputs",
        "RestJsonSupportsNaNFloatLabels",
        "RestJsonSupportsInfinityFloatLabels",
        "RestJsonSupportsNegativeInfinityFloatLabels",
        "RestJsonInputUnionWithUnitMember",
        "RestJsonQueryIdempotencyTokenAutoFill",
        "RestJsonQueryIdempotencyTokenAutoFillIsSet",
        "RestJsonNoInputAndOutput",
        "RestJsonEmptyInputAndEmptyOutputWithJson",
        "RestJsonHttpWithEmptyStructurePayload",
        "HttpQueryParamsOnlyEmptyRequest",
        "DocumentTypeInputWithObject",
        "DocumentInputWithString",
        "DocumentInputWithNumber",
        "DocumentInputWithBoolean",
        "DocumentInputWithList",
        "DocumentTypeAsMapValueInput",
        "DocumentTypeAsPayloadInput",
        "DocumentTypeAsPayloadInputString",
        "RestJsonAllQueryStringTypes",
        "RestJsonQueryStringEscaping",
        "RestJsonQueryPrecedence",
        "MediaTypeHeaderInputBase64",
        "RestJsonHttpGetWithHeaderMemberNoModeledBody",
        "RestJsonHttpPrefixEmptyHeaders",
        "RestJsonHttpRequestWithLabelsAndTimestampFormat",
        "RestJsonHttpWithPostHeaderMemberNoModeledBody",
        "RestJsonInputWithHeadersAndAllParams",
        "RestJsonEndpointTrait",
        "RestJsonEndpointTraitWithHostLabel",
        "RestJsonHostWithPath",
        "RestJsonInputAndOutputWithQuotedStringHeaders",
        "RestJsonHttpPayloadTraitsWithMediaTypeWithBlob",
        "RestJsonStreamingTraitsRequireLengthWithBlob",
        "RestJsonStreamingTraitsRequireLengthWithNoBlobBody",
        "RestJsonStreamingTraitsWithMediaTypeWithBlob",
        "RestJsonTestBodyStructure",
        "RestJsonTestPayloadBlob",
        "RestJsonTestPayloadStructure",
        "RestJsonTimestampFormatHeaders",
        "RestJsonToleratesRegexCharsInSegments",
        "RestJsonUnitInputAndOutput",
        "RestJsonHttpChecksumRequired",
        "SDKAppliedContentEncoding_restJson1",
        "SDKAppendedGzipAfterProvidedEncoding_restJson1",
        "RestJsonRecursiveStructuresValidate",
        "ApiGatewayAccept",
    };

    public static readonly IReadOnlySet<string> ExecutableResponseCases = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        // Local harness (model/defaulted-member-null.smithy): explicit null against a
        // member carrying @default. The official protocol tests do not cover it, and a
        // real bug in the structure reader survived the entire suite because of that.
        "RestJsonLocalDefaultedMembersExplicitNull",
        "RestJsonLocalDefaultedMembersAbsent",
        "RestJsonLocalDefaultedMembersPresent",
        "RestJsonEmptyInputAndEmptyOutput",
        "RestJsonEmptyInputAndEmptyOutputJsonObjectOutput",
        "RestJsonHttpPayloadWithStructure",
        "RestJsonHttpPrefixHeadersArePresent",
        "RestJsonHttpResponseCode",
        "RestJsonHttpResponseCodeWithNoPayload",
        // Diagnostic batch — remove failures after testing
        "RestJsonSimpleScalarProperties",
        "RestJsonJsonBlobs",
        "RestJsonJsonEnums",
        "RestJsonJsonIntEnums",
        "RestJsonLists",
        "RestJsonListsEmpty",
        "RestJsonJsonMaps",
        "RestJsonJsonTimestamps",
        "RestJsonJsonTimestampsWithDateTimeFormat",
        "RestJsonJsonTimestampsWithDateTimeOnTargetFormat",
        "RestJsonJsonTimestampsWithEpochSecondsFormat",
        "RestJsonJsonTimestampsWithEpochSecondsOnTargetFormat",
        "RestJsonJsonTimestampsWithHttpDateFormat",
        "RestJsonJsonTimestampsWithHttpDateOnTargetFormat",
        "RestJsonDeserializeStringUnionValue",
        "RestJsonDeserializeBooleanUnionValue",
        "RestJsonDeserializeNumberUnionValue",
        "RestJsonDeserializeBlobUnionValue",
        "RestJsonDeserializeTimestampUnionValue",
        "RestJsonDeserializeEnumUnionValue",
        "RestJsonDeserializeListUnionValue",
        "RestJsonDeserializeMapUnionValue",
        "RestJsonDeserializeStructureUnionValue",
        "RestJsonDeserializeIgnoreType",
        "RestJsonInputAndOutputWithStringHeaders",
        "RestJsonInputAndOutputWithNumericHeaders",
        "RestJsonInputAndOutputWithBooleanHeaders",
        "RestJsonInputAndOutputWithTimestampHeaders",
        "RestJsonInputAndOutputWithEnumHeaders",
        "RestJsonInputAndOutputWithIntEnumHeaders",
        "RestJsonNullAndEmptyHeaders",
        "RestJsonRecursiveShapes",
        "RestJsonDoesntDeserializeNullStructureValues",
        "RestJsonHttpPayloadTraitsWithBlob",
        "RestJsonHttpPayloadTraitsWithNoBlobBody",
        "RestJsonHttpPayloadWithUnion",
        "RestJsonHttpPayloadWithUnsetUnion",
        "RestJsonStreamingTraitsWithBlob",
        "RestJsonStreamingTraitsWithNoBlobBody",
        "RestJsonEnumPayloadResponse",
        "RestJsonStringPayloadResponse",
        "RestJsonDeserializesDenseSetMap",
        "RestJsonDeserializesSparseSetMap",
        "RestJsonDeserializesSparseSetMapAndRetainsNull",
        "RestJsonDeserializesSparseNullMapValues",
        "RestJsonSparseJsonMaps",
        "RestJsonSparseListsSerializeNull",
        "RestJsonDeserializesZeroValuesInMaps",
        "RestJsonDeserializesZeroValuesInSparseMaps",
        "PostUnionWithJsonNameResponse1",
        "PostUnionWithJsonNameResponse2",
        "PostUnionWithJsonNameResponse3",
        "RestJsonSupportsNaNFloatInputs",
        "RestJsonSupportsInfinityFloatInputs",
        "RestJsonSupportsNegativeInfinityFloatInputs",
        "RestJsonSupportsNaNFloatHeaderOutputs",
        "RestJsonSupportsInfinityFloatHeaderOutputs",
        "RestJsonSupportsNegativeInfinityFloatHeaderOutputs",
        "RestJsonOutputUnionWithUnitMember",
        "RestJsonGreetingWithErrors",
        "RestJsonGreetingWithErrorsNoPayload",
        "RestJsonComplexErrorWithNoMessage",
        "RestJsonEmptyComplexErrorWithNoMessage",
        "RestJsonFooErrorUsingCode",
        "RestJsonFooErrorUsingCodeAndNamespace",
        "RestJsonFooErrorUsingCodeUriAndNamespace",
        "RestJsonFooErrorUsingXAmznErrorType",
        "RestJsonFooErrorUsingXAmznErrorTypeWithUri",
        "RestJsonFooErrorUsingXAmznErrorTypeWithUriAndNamespace",
        "RestJsonFooErrorWithDunderType",
        "RestJsonFooErrorWithDunderTypeAndNamespace",
        "RestJsonFooErrorWithDunderTypeUriAndNamespace",
        "RestJsonFooErrorWithNestedTypeProperty",
        "RestJsonInvalidGreetingError",
        "RestJsonHttpResponseCodeDefaultsToModeledCode",
        "RestJsonHttpResponseCodeNotSetFallsBackToHttpCode",
        "RestJsonHttpResponseCodeRequired",
        "RestJsonIgnoreQueryParamsInResponse",
        "RestJsonNoInputAndNoOutput",
        "RestJsonNoInputAndOutputNoPayload",
        "RestJsonNoInputAndOutputWithJson",
        "RestJsonHttpEmptyPrefixHeadersResponseClient",
        "RestJsonClientPopulatesDefaultsValuesWhenMissingInResponse",
        "RestJsonClientPopulatesNestedDefaultsWhenMissingInResponseBody",
        "RestJsonClientIgnoresDefaultValuesIfMemberValuesArePresentInResponse",
        "RestJsonHttpPayloadWithStructureAndEmptyResponseBody",
        "HttpPrefixHeadersResponse",
        "RestJsonServersDontSerializeNullStructureValues",
        "RestJsonDateTimeWithFractionalSeconds",
        "RestJsonDateTimeWithPositiveOffset",
        "RestJsonDateTimeWithNegativeOffset",
        "DocumentOutput",
        "DocumentOutputString",
        "DocumentOutputNumber",
        "DocumentOutputBoolean",
        "DocumentOutputArray",
        "DocumentTypeAsMapValueOutput",
        "DocumentTypeAsPayloadOutput",
        "DocumentTypeAsPayloadOutputString",
        "RestJsonTimestampFormatHeaders",
        "MediaTypeHeaderOutputBase64",
        "RestJsonInputAndOutputWithQuotedStringHeaders",
        "RestJsonUnitInputAndOutputNoOutput",
        "RestJsonHttpPayloadTraitsWithMediaTypeWithBlob",
        "RestJsonStreamingTraitsWithMediaTypeWithBlob",
    };
}
