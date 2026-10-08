using NSmithy.Codecs.Cbor;
using NSmithy.Codecs.Json;
using NSmithy.Codecs.Proto;
using NSmithy.Codecs.Xml;
using NSmithy.Core.Serde;
using Nsmithy.Tests.Core;

namespace NSmithy.Tests.Codecs;

public sealed class CodecFactoryTests
{
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
        var codec = JsonCodecFactory.Default.FromMember(
            TimestampTarget(member),
            member.MemberTraits
        );
        var value = new DateTimeOffset(2026, 8, 9, 12, 34, 56, TimeSpan.Zero);

        var json = codec.SerializeText(value);

        Assert.Equal("\"2026-08-09T12:34:56Z\"", json);
        Assert.Equal(value, codec.DeserializeText(json));
    }

    [Fact]
    public void XmlFactoryRetainsTraitsFromTargetedMember()
    {
        var member = TimestampMember();
        var codec = XmlCodecFactory.Default.FromMember(
            TimestampTarget(member),
            member.MemberTraits
        );
        var value = new DateTimeOffset(2026, 8, 9, 12, 34, 56, TimeSpan.Zero);

        var xml = codec.SerializeText(value);

        Assert.Equal("<CreatedAt>2026-08-09T12:34:56Z</CreatedAt>", xml);
        Assert.Equal(value, codec.DeserializeText(xml));
    }

    private static MemberSchema TimestampMember() =>
        Assert.IsAssignableFrom<IStructSchema>(TimestampPayloadSchema.Schema).GetMember("value")!;

    private static Schema<DateTimeOffset?> TimestampTarget(MemberSchema member) =>
        Assert.IsAssignableFrom<Schema<DateTimeOffset?>>(member.Target);
}
