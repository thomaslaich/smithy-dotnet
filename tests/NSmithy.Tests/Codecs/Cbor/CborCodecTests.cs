using System.Formats.Cbor;
using NSmithy.Codecs.Cbor;
using NSmithy.Core.Serde;
using Nsmithy.Tests.Cbor;

namespace NSmithy.Tests.Codecs.Cbor;

public sealed class CborCodecTests
{
    [Fact]
    public void CborCodecRoundTripsNestedStructure()
    {
        var input = new Person(Name: "Ada", Age: 36, Address: new Address("London"));
        var codec = CborCodecFactory.Default.FromSchema(PersonSchema.Schema);

        var bytes = codec.Serialize(input);
        var decoded = codec.Deserialize(bytes);

        Assert.Equal(input, decoded);
    }

    [Fact]
    public void CborCodecReadsHalfPrecisionFloat()
    {
        var writer = new CborWriter();
        writer.WriteHalf((Half)1.5);
        var codec = CborCodecFactory.Default.FromSchema(Schemas.Float);

        var decoded = codec.Deserialize(writer.Encode());

        Assert.Equal(1.5f, decoded);
    }

    [Fact]
    public void CborCodecResetsReusedWriterBetweenSerializations()
    {
        var codec = CborCodecFactory.Default.FromSchema(Schemas.String);

        _ = codec.Serialize("a longer first value");
        var bytes = codec.Serialize("x");

        var reader = new CborReader(bytes);
        Assert.Equal("x", reader.ReadTextString());
        Assert.Equal(CborReaderState.Finished, reader.PeekState());
    }
}
