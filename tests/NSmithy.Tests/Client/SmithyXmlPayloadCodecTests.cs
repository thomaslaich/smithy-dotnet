using System.Globalization;
using NSmithy.Codecs.Xml;
using NSmithy.Core;
using NSmithy.Core.Annotations;

namespace NSmithy.Tests.Client;

public sealed class SmithyXmlPayloadCodecTests
{
    [Fact]
    public void SerializeUsesXmlMemberNamesAndFlattenedCollections()
    {
        var payload = new ForecastResponse(
            "clear",
            DateTimeOffset.Parse("2026-04-23T10:15:30Z", CultureInfo.InvariantCulture),
            new ForecastTags(["north", "windy"])
        );

        var xml = System.Text.Encoding.UTF8.GetString(
            SmithyXmlPayloadCodec.Default.Serialize(payload)
        );

        Assert.Equal(
            "<ForecastResponse><Condition>clear</Condition><GeneratedAt>2026-04-23T10:15:30.0000000+00:00</GeneratedAt><Tag>north</Tag><Tag>windy</Tag></ForecastResponse>",
            xml
        );
    }

    [Fact]
    public void DeserializeRoundTripsStructuresWithXmlTraits()
    {
        var xml = """
            <ForecastResponse>
              <Condition>clear</Condition>
              <GeneratedAt>2026-04-23T10:15:30.0000000+00:00</GeneratedAt>
              <Tag>north</Tag>
              <Tag>windy</Tag>
            </ForecastResponse>
            """;

        var payload = SmithyXmlPayloadCodec.Default.Deserialize<ForecastResponse>(
            System.Text.Encoding.UTF8.GetBytes(xml)
        );

        Assert.Equal("clear", payload.Summary);
        Assert.Equal(
            DateTimeOffset.Parse("2026-04-23T10:15:30.0000000+00:00", CultureInfo.InvariantCulture),
            payload.GeneratedAt
        );
        Assert.Equal(["north", "windy"], payload.Tags.Values);
    }

    [SmithyShape("example.weather#ForecastResponse", ShapeKind.Structure)]
    private sealed record class ForecastResponse(
        [property: SmithyMember("summary", "smithy.api#String", IsRequired = true)]
        [property: SmithyTrait("smithy.api#xmlName", Value = "Condition")]
            string Summary,
        [property: SmithyMember("generatedAt", "smithy.api#Timestamp", IsRequired = true)]
        [property: SmithyTrait("smithy.api#xmlName", Value = "GeneratedAt")]
            DateTimeOffset GeneratedAt,
        [property: SmithyMember("tags", "example.weather#ForecastTags", IsRequired = true)]
        [property: SmithyTrait("smithy.api#xmlFlattened")]
        [property: SmithyTrait("smithy.api#xmlName", Value = "Tag")]
            ForecastTags Tags
    );

    [SmithyShape("example.weather#ForecastTags", ShapeKind.List)]
    private sealed record class ForecastTags(
        [property: SmithyMember("member", "smithy.api#String", IsRequired = true)]
        [property: SmithyTrait("smithy.api#xmlName", Value = "Tag")]
            IReadOnlyList<string> Values
    );
}
