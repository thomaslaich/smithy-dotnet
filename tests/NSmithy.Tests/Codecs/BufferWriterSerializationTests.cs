using System.Buffers;
using NSmithy.Codecs.Cbor;
using NSmithy.Codecs.Json;
using NSmithy.Codecs.Proto;
using NSmithy.Core.Serde;
using NSmithy.Http;
using Nsmithy.Tests.Proto;
using Nsmithy.Tests.Proto.Schemas;

namespace NSmithy.Tests.Codecs;

public sealed class BufferWriterSerializationTests
{
    public static TheoryData<string> Formats => ["json", "cbor", "proto"];

    public static TheoryData<string> ProjectionFormats => ["json", "cbor"];

    [Theory]
    [MemberData(nameof(Formats))]
    public void WritingIntoABufferAppendsWhatTheArrayHolds(string format)
    {
        var codec = Factory(format).FromSchema(SimpleSchema.Schema);
        // Larger than the buffer's first array, so the codec's write makes it grow.
        var value = new Simple(new string('x', 1000), 300);
        using var buffer = new SmithyPooledBuffer(1);
        buffer.Write("ab"u8);

        codec.Serialize(value, buffer);

        Assert.Equal([.. "ab"u8, .. codec.Serialize(value)], buffer.WrittenMemory.ToArray());
    }

    [Theory]
    [MemberData(nameof(ProjectionFormats))]
    public void AProjectionWritesIntoABufferWhatTheArrayHolds(string format)
    {
        var codec = ((IProjectionCodecFactory)Factory(format)).FromProjection(
            Schemas.Project(SimpleSchema.Schema, name => name == "name")
        );
        var value = new Simple("hi", 300);
        using var buffer = new SmithyPooledBuffer();

        codec.Serialize(value, buffer);

        Assert.Equal(codec.Serialize(value), buffer.WrittenMemory.ToArray());
    }

    [Fact]
    public void ACodecWithoutABufferWriteOfItsOwnCopiesItsArrayIn()
    {
        ICodec<string> codec = new ArrayOnlyCodec();
        using var buffer = new SmithyPooledBuffer();

        codec.Serialize("abc", buffer);

        Assert.Equal("abc"u8.ToArray(), buffer.WrittenMemory.ToArray());
    }

    private static ICodecFactory Factory(string format) =>
        format switch
        {
            "json" => JsonCodecFactory.Default,
            "cbor" => CborCodecFactory.Default,
            _ => ProtoCodecFactory.Default,
        };

    private sealed class ArrayOnlyCodec : ICodec<string>
    {
        public byte[] Serialize(string value) => System.Text.Encoding.UTF8.GetBytes(value);

        public string Deserialize(byte[] payload) => System.Text.Encoding.UTF8.GetString(payload);
    }
}
