using RpcV2.Conformance;

namespace RpcV2Json.Conformance;

/// <summary>
/// Every official rpcv2Json case applicable to a side runs, except the known gaps listed here.
/// A gap stays in the conformance denominator but not the numerator.
/// </summary>
internal static class RpcV2JsonAllowlist
{
    public const string Protocol = "smithy.protocols#rpcv2Json";

    /// <summary>Client-request cases the generated client fails today.</summary>
    public static readonly IReadOnlySet<string> KnownRequestGaps = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        // Document values. Untriaged: the runner cannot yet bind or compare `Document` params, so some may be runner gaps.
        "RpcV2JsonDocumentMembersArray",
        "RpcV2JsonDocumentMembersBoolean",
        "RpcV2JsonDocumentMembersJsonObject",
        "RpcV2JsonDocumentMembersNestedStruct",
        "RpcV2JsonDocumentMembersNumber",
        "RpcV2JsonDocumentMembersString",
        "RpcV2JsonDocumentUnionDocumentValue",
        "RpcV2JsonListOfDocumentsMixedTypes",
        "RpcV2JsonMapOfDocumentsMixedTypes",
        // Discriminated unions: the discriminator is not written or read.
        "RpcV2JsonRequestDiscriminatedUnion",
        "RpcV2JsonRequestDiscriminatedUnionDefaultDiscriminator",
        "RpcV2JsonRequestDiscriminatedUnionList",
        "RpcV2JsonRequestDiscriminatedUnionMultipleMembers",
        "RpcV2JsonRequestNestedDiscriminatedUnion",
        "RpcV2JsonRequestStandardUnionInDiscriminatedUnion",
        // Blobs nested in lists and maps. Untriaged.
        "RpcV2JsonListOfListsSerialize",
        "RpcV2JsonListOfMapsSerialize",
        "RpcV2JsonMapOfListsSerialize",
        "RpcV2JsonMapOfMapsSerialize",
        // bigDecimal values beyond C# decimal precision.
        "RpcV2JsonScalarMembersArbitraryPrecision",
        // Idempotency tokens: the runner does not inject the fixture's token provider.
        "RpcV2JsonIdempotencyTokenAutoFill",
        // @requestCompression: the runner compares the compressed body as JSON.
        "RpcV2JsonRequestCompressionGzip",
    };

    /// <summary>Client-response cases the generated client fails today.</summary>
    public static readonly IReadOnlySet<string> KnownResponseGaps = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        // Document values. Untriaged: the runner cannot yet bind or compare `Document` params, so some may be runner gaps.
        "RpcV2JsonDocumentMembersDeserializeArray",
        "RpcV2JsonDocumentMembersDeserializeBoolean",
        "RpcV2JsonDocumentMembersDeserializeJsonObject",
        "RpcV2JsonDocumentMembersDeserializeNestedStruct",
        "RpcV2JsonDocumentMembersDeserializeNumber",
        "RpcV2JsonDocumentMembersDeserializeString",
        "RpcV2JsonListOfDocumentsDeserialize",
        "RpcV2JsonMapOfDocumentsDeserialize",
        // Discriminated unions: the discriminator is not written or read.
        "RpcV2JsonResponseDiscriminatedUnion",
        "RpcV2JsonResponseDiscriminatedUnionDefaultDiscriminator",
        "RpcV2JsonResponseDiscriminatedUnionDiscriminatorInMiddle",
        "RpcV2JsonResponseDiscriminatedUnionDiscriminatorLast",
        "RpcV2JsonResponseDiscriminatedUnionIgnoresUnknownFields",
        "RpcV2JsonResponseDiscriminatedUnionList",
        "RpcV2JsonResponseNestedDiscriminatedUnionDiscriminatorsLast",
        "RpcV2JsonResponseStandardUnionInDiscriminatedUnion",
        // Blobs nested in lists and maps. Untriaged.
        "RpcV2JsonListOfListsDeserialize",
        "RpcV2JsonListOfMapsDeserialize",
        "RpcV2JsonMapOfListsDeserialize",
        "RpcV2JsonMapOfMapsDeserialize",
    };

    /// <summary>Server-request cases the generated server fails today.</summary>
    public static readonly IReadOnlySet<string> KnownServerRequestGaps = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        // Document values. Untriaged: the runner cannot yet bind or compare `Document` params, so some may be runner gaps.
        "RpcV2JsonDocumentMembersArray",
        "RpcV2JsonDocumentMembersBoolean",
        "RpcV2JsonDocumentMembersJsonObject",
        "RpcV2JsonDocumentMembersNestedStruct",
        "RpcV2JsonDocumentMembersNumber",
        "RpcV2JsonDocumentMembersString",
        "RpcV2JsonListOfDocumentsMixedTypes",
        "RpcV2JsonMapOfDocumentsMixedTypes",
        // Discriminated unions: the discriminator is not written or read.
        "RpcV2JsonRequestDiscriminatedUnion",
        "RpcV2JsonRequestDiscriminatedUnionDefaultDiscriminator",
        "RpcV2JsonRequestDiscriminatedUnionDiscriminatorLast",
        "RpcV2JsonRequestDiscriminatedUnionList",
        "RpcV2JsonRequestDiscriminatedUnionMultipleMembers",
        "RpcV2JsonRequestNestedDiscriminatedUnion",
        "RpcV2JsonRequestNestedDiscriminatedUnionDiscriminatorsLast",
        "RpcV2JsonRequestStandardUnionInDiscriminatedUnion",
        // Blobs nested in lists and maps. Untriaged.
        "RpcV2JsonListOfListsSerialize",
        "RpcV2JsonListOfMapsSerialize",
        "RpcV2JsonMapOfListsSerialize",
        "RpcV2JsonMapOfMapsSerialize",
        // @requestCompression: the runner compares the compressed body as JSON.
        "RpcV2JsonRequestCompressionGzip",
        // Empty and missing bodies. Untriaged.
        "RpcV2JsonEmptyInputOutputServerAllowsEmptyBody",
        "RpcV2JsonNoInputOutputSerialize",
    };

    /// <summary>Server-response cases the generated server fails today.</summary>
    public static readonly IReadOnlySet<string> KnownServerResponseGaps = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        // Document values. Untriaged: the runner cannot yet bind or compare `Document` params, so some may be runner gaps.
        "RpcV2JsonDocumentMembersDeserializeArray",
        "RpcV2JsonDocumentMembersDeserializeBoolean",
        "RpcV2JsonDocumentMembersDeserializeJsonObject",
        "RpcV2JsonDocumentMembersDeserializeNestedStruct",
        "RpcV2JsonDocumentMembersDeserializeNull",
        "RpcV2JsonDocumentMembersDeserializeNumber",
        "RpcV2JsonDocumentMembersDeserializeString",
        "RpcV2JsonDocumentUnionDeserializeDocumentValue",
        "RpcV2JsonListOfDocumentsDeserialize",
        "RpcV2JsonMapOfDocumentsDeserialize",
        // Discriminated unions: the discriminator is not written or read.
        "RpcV2JsonResponseDiscriminatedUnion",
        "RpcV2JsonResponseDiscriminatedUnionDefaultDiscriminator",
        "RpcV2JsonResponseDiscriminatedUnionList",
        "RpcV2JsonResponseStandardUnionInDiscriminatedUnion",
        // Blobs nested in lists and maps. Untriaged.
        "RpcV2JsonListOfListsDeserialize",
        "RpcV2JsonListOfMapsDeserialize",
        "RpcV2JsonMapOfListsDeserialize",
        "RpcV2JsonMapOfMapsDeserialize",
        // bigDecimal values beyond C# decimal precision.
        "RpcV2JsonScalarMembersArbitraryPrecisionDeserialize",
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
