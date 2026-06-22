namespace RestXml.Conformance;

/// <summary>
/// Allowlist of <c>httpRequestTests</c> / <c>httpResponseTests</c> case ids that the generated
/// restXml client is currently expected to satisfy. Empty until cases are individually verified.
/// </summary>
internal static class RestXmlAllowlist
{
    public const string Protocol = "aws.protocols#restXml";

    public static readonly IReadOnlySet<string> ExecutableRequestCases = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "EmptyInputAndEmptyOutput",
        "NoInputAndNoOutput",
        "NoInputAndOutput",
        "RestXmlOmitsNullQuery",
    };

    public static readonly IReadOnlySet<string> ExecutableResponseCases = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "EmptyInputAndEmptyOutput",
        "HttpPayloadTraitsWithBlob",
        "HttpPayloadTraitsWithNoBlobBody",
        "HttpPayloadWithStructure",
        "HttpPrefixHeadersArePresent",
        "HttpPrefixHeadersAreNotPresent",
        "RestXmlHttpResponseCode",
        "IgnoreQueryParamsInResponse",
        "InputAndOutputWithStringHeaders",
        "InputAndOutputWithNumericHeaders",
        "InputAndOutputWithBooleanHeaders",
        "InputAndOutputWithTimestampHeaders",
        "InputAndOutputWithEnumHeaders",
        "NoInputAndNoOutput",
        "NoInputAndOutput",
        "SimpleScalarProperties",
        "SimpleScalarPropertiesComplexEscapes",
        "SimpleScalarPropertiesWithEscapedCharacter",
        "SimpleScalarPropertiesWithXMLPreamble",
        "SimpleScalarPropertiesWithWhiteSpace",
        "SimpleScalarPropertiesPureWhiteSpace",
        "TimestampFormatHeaders",
        "XmlBlobs",
        "XmlEmptyBlobs",
        "XmlEmptySelfClosedBlobs",
        "XmlEmptyLists",
        "XmlEmptyMaps",
        "XmlEmptySelfClosedMaps",
        "XmlEmptyStrings",
        "XmlEmptySelfClosedStrings",
        "XmlEnums",
        "XmlIntEnums",
        "XmlLists",
        "XmlMaps",
        "XmlMapsXmlName",
        "XmlTimestamps",
        "XmlTimestampsWithDateTimeFormat",
        "XmlTimestampsWithDateTimeOnTargetFormat",
        "XmlTimestampsWithEpochSecondsFormat",
        "XmlTimestampsWithEpochSecondsOnTargetFormat",
        "XmlTimestampsWithHttpDateFormat",
        "XmlTimestampsWithHttpDateOnTargetFormat",
    };
}
