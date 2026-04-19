using Smithy.NET.Core;
using Smithy.NET.Core.Annotations;
using Smithy.NET.Json;

namespace Smithy.NET.Tests.Json;

public sealed class SmithyJsonSerializerTests
{
    [Fact]
    public void SerializeUsesSmithyMemberNamesAndJsonNameTrait()
    {
        var payload = new ForecastRequest(
            "Zurich",
            WeatherKind.Sunny,
            new ForecastTags(new Dictionary<string, string?> { ["region"] = "ch" }),
            ForecastValue.FromText("clear"),
            Document.From(new Dictionary<string, Document> { ["source"] = Document.From("test") })
        );

        var json = SmithyJsonSerializer.Serialize(payload);

        Assert.Equal(
            """{"city":"Zurich","kind":"SUN","meta":{"source":"test"},"tags":{"region":"ch"},"value":{"text":"clear"}}""",
            json
        );
    }

    [Fact]
    public void DeserializeAppliesConstructorDefaultsAndPreservesUnknownUnionVariants()
    {
        var request = SmithyJsonSerializer.Deserialize<ForecastRequest>(
            """{"city":"Zurich","kind":"hail","value":{"radar":{"url":"example"}},"meta":null}"""
        );

        Assert.Equal("Zurich", request.City);
        Assert.Equal("hail", request.Kind.Value);
        Assert.Null(request.Tags);
        var unknown = Assert.IsType<ForecastValue.Unknown>(request.Value);
        Assert.Equal("radar", unknown.Tag);
        Assert.Equal("example", unknown.Value.AsObject()["url"].AsString());
        Assert.Equal(DocumentKind.Null, request.Metadata.Kind);
    }

    [SmithyShape("example.weather#ForecastRequest", ShapeKind.Structure)]
    private sealed partial record class ForecastRequest
    {
        public ForecastRequest(
            string city,
            WeatherKind kind,
            ForecastTags? tags,
            ForecastValue value,
            Document metadata
        )
        {
            City = city;
            Kind = kind;
            Tags = tags;
            Value = value;
            Metadata = metadata;
        }

        [SmithyMember("city", "smithy.api#String", IsRequired = true)]
        public string City { get; }

        [SmithyMember("kind", "example.weather#WeatherKind", IsRequired = true)]
        public WeatherKind Kind { get; }

        [SmithyMember("tags", "example.weather#ForecastTags")]
        public ForecastTags? Tags { get; }

        [SmithyMember("value", "example.weather#ForecastValue", IsRequired = true)]
        public ForecastValue Value { get; }

        [SmithyMember("metadata", "smithy.api#Document", JsonName = "meta")]
        public Document Metadata { get; }
    }

    [SmithyShape("example.weather#WeatherKind", ShapeKind.Enum)]
    private readonly partial record struct WeatherKind(string Value)
    {
        [SmithyEnumValue("SUN")]
        public static WeatherKind Sunny { get; } = new("SUN");
    }

    [SmithyShape("example.weather#ForecastTags", ShapeKind.Map)]
    private sealed partial record class ForecastTags
    {
        public ForecastTags(IReadOnlyDictionary<string, string?> values)
        {
            Values = values;
        }

        [SmithyMember("value", "smithy.api#String", IsSparse = true)]
        public IReadOnlyDictionary<string, string?> Values { get; }
    }

    [SmithyShape("example.weather#ForecastValue", ShapeKind.Union)]
    private abstract partial record class ForecastValue
    {
        private protected ForecastValue() { }

        [SmithyMember("text", "smithy.api#String")]
        public sealed partial record class Text : ForecastValue
        {
            public Text(string value)
            {
                Value = value;
            }

            public string Value { get; }
        }

        public static Text FromText(string value)
        {
            return new Text(value);
        }

        public sealed partial record class Unknown : ForecastValue
        {
            public Unknown(string tag, Document value)
            {
                Tag = tag;
                Value = value;
            }

            public string Tag { get; }

            public Document Value { get; }
        }
    }
}
