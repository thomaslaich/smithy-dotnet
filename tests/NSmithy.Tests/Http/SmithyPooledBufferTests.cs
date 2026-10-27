using System.Buffers;
using NSmithy.Http;

namespace NSmithy.Tests.Http;

public sealed class SmithyPooledBufferTests
{
    [Fact]
    public void GrowingKeepsWhatWasWritten()
    {
        using var buffer = new SmithyPooledBuffer(1);
        var content = Enumerable.Range(0, 10_000).Select(i => (byte)i).ToArray();

        foreach (var chunk in content.Chunk(333))
        {
            buffer.Write(chunk);
        }

        Assert.Equal(content.Length, buffer.WrittenCount);
        Assert.Equal(content, buffer.WrittenMemory.ToArray());
    }

    [Fact]
    public void AResponseReturnsItsBufferWhenDisposed()
    {
        var buffer = new SmithyPooledBuffer();
        buffer.Write("abc"u8);
        var response = new SmithyHttpServerResponse
        {
            Body = SmithyHttpServerResponse.SingleChunk(buffer),
        };

        Assert.True(response.TryGetSingleChunk(out var content));
        Assert.Equal("abc"u8.ToArray(), content.ToArray());

        response.Dispose();

        Assert.Throws<ObjectDisposedException>(() => buffer.WrittenMemory);
    }

    [Fact]
    public void AdvancingPastTheSpanIsRejected()
    {
        using var buffer = new SmithyPooledBuffer();
        var span = buffer.GetSpan();

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Advance(span.Length + 1));
    }
}
