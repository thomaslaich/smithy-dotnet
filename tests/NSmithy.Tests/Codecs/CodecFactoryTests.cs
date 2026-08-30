using NSmithy.Codecs.Cbor;
using NSmithy.Codecs.Json;
using NSmithy.Codecs.Proto;
using NSmithy.Codecs.Xml;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Tests.Codecs;

public sealed class CodecFactoryTests
{
    private static readonly ShapeId TimestampFormat = ShapeId.Parse("smithy.api#timestampFormat");
    private static readonly ShapeId XmlName = ShapeId.Parse("smithy.api#xmlName");

    [Fact]
    public void FactoriesExposeTheirSupportedCapabilities()
    {
        Assert.IsAssignableFrom<IProjectionCodecFactory>(JsonCodecFactory.Default);
        Assert.IsAssignableFrom<IProjectionCodecFactory>(XmlCodecFactory.Default);
        Assert.IsAssignableFrom<IProjectionCodecFactory>(CborCodecFactory.Default);
        Assert.IsAssignableFrom<ICodecFactory>(ProtoCodecFactory.Default);
        Assert.IsNotAssignableFrom<IProjectionCodecFactory>(ProtoCodecFactory.Default);
    }

    [Fact]
    public void JsonFactoryRetainsTraitsFromTargetedMember()
    {
        var member = TimestampMember();
        var codec = JsonCodecFactory.Default.FromMember(member);
        var value = new DateTimeOffset(2026, 8, 9, 12, 34, 56, TimeSpan.Zero);

        var json = codec.SerializeText(value);

        Assert.Equal("\"2026-08-09T12:34:56Z\"", json);
        Assert.Equal(value, codec.DeserializeText(json));
    }

    [Fact]
    public void XmlFactoryRetainsTraitsFromTargetedMember()
    {
        var member = TimestampMember();
        var codec = XmlCodecFactory.Default.FromMember(member);
        var value = new DateTimeOffset(2026, 8, 9, 12, 34, 56, TimeSpan.Zero);

        var xml = codec.SerializeText(value);

        Assert.Equal("<CreatedAt>2026-08-09T12:34:56Z</CreatedAt>", xml);
        Assert.Equal(value, codec.DeserializeText(xml));
    }

    private static ITypedTargetMemberSchema<DateTimeOffset> TimestampMember()
    {
        var schema = Schemas
            .Structure<TimestampPayload, TimestampPayloadBuilder>(
                new ShapeId("example", "TimestampPayload")
            )
            .Required(
                "value",
                static payload => payload.Value,
                static (builder, value) => builder.Value = value,
                Schemas.Timestamp,
                [
                    new Trait(TimestampFormat, Document.From("date-time")),
                    new Trait(XmlName, Document.From("CreatedAt")),
                ]
            )
            .Build(
                static () => new TimestampPayloadBuilder(),
                static builder => new TimestampPayload(builder.Value)
            );
        return Assert.IsAssignableFrom<ITypedTargetMemberSchema<DateTimeOffset>>(
            schema.GetMember("value")
        );
    }

    private sealed record TimestampPayload(DateTimeOffset Value);

    private sealed class TimestampPayloadBuilder
    {
        public DateTimeOffset Value { get; set; }
    }
}
