using System.Globalization;
using SmithyNet.Core;
using SmithyNet.Core.Annotations;
using SmithyNet.Codecs.Xml;

namespace SmithyNet.Tests.Xml;

public sealed class SmithyXmlSerializerTests
{
    [Fact]
    public void SerializeUsesXmlMemberNamesAndFlattenedCollections()
    {
        var payload = new ForecastResponse(
            "clear",
            DateTimeOffset.Parse("2026-04-23T10:15:30Z", CultureInfo.InvariantCulture),
            new ForecastTags(["north", "windy"])
        );

        var xml = SmithyXmlSerializer.Serialize(payload);

        Assert.Equal(
            "<ForecastResponse><Condition>clear</Condition><GeneratedAt>2026-04-23T10:15:30.0000000+00:00</GeneratedAt><Tag>north</Tag><Tag>windy</Tag></ForecastResponse>",
            xml
        );
    }

    [Fact]
    public void DeserializeMembersReadsSingleMemberFromXmlDocument()
    {
        var xml =
            "<ForecastResponse><Condition>clear</Condition><GeneratedAt>2026-04-23T10:15:30.0000000+00:00</GeneratedAt></ForecastResponse>";

        var summary = SmithyXmlSerializer.DeserializeMember<string>(xml, "Condition");

        Assert.Equal("clear", summary);
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
