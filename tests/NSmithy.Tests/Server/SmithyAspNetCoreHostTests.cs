using System.Text;
using Microsoft.AspNetCore.Http;
using NSmithy.Http;
using NSmithy.Protocols.RestJson;
using NSmithy.Server;
using NSmithy.Server.AspNetCore;
using Nsmithy.Tests.Server;
using Nsmithy.Tests.Server.Schemas;

namespace NSmithy.Tests.Server;

public sealed class SmithyAspNetCoreHostTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(10, true)]
    [InlineData(200_000, true)]
    [InlineData(200_000, false)]
    public async Task ToSmithyRequestReadsTheWholeBody(int length, bool declaresLength)
    {
        var content = Enumerable.Range(0, length).Select(i => (byte)i).ToArray();
        var httpContext = Post(new MemoryStream(content));
        httpContext.Request.ContentLength = declaresLength ? length : null;

        var request = await SmithyAspNetCoreHost.ToSmithyRequestAsync(httpContext);

        Assert.Equal(content, request.Body.BufferedContent);
    }

    [Fact]
    public async Task ToSmithyRequestDoesNotReadABodyDeclaredEmpty()
    {
        var httpContext = Post(new UnreadableStream());
        httpContext.Request.ContentLength = 0;

        var request = await SmithyAspNetCoreHost.ToSmithyRequestAsync(httpContext);

        Assert.Empty(request.Body.BufferedContent);
    }

    [Fact]
    public async Task ToSmithyRequestKeepsEveryValueOfAHeader()
    {
        var httpContext = Post(Stream.Null);
        httpContext.Request.Headers["X-One"] = "a";
        httpContext.Request.Headers["X-Many"] = new(["b", "c"]);

        var request = await SmithyAspNetCoreHost.ToSmithyRequestAsync(httpContext);

        Assert.Equal(["a"], request.Headers["X-One"]);
        Assert.Equal(["b", "c"], request.Headers["X-Many"]);
    }

    [Fact]
    public async Task DispatchWritesAUnaryResponse()
    {
        var protocol = new RestJson1Protocol()
            .ForService(FixturesSchema.Schema)
            .ForServerOperation(CreateUserSchema.Schema);
        var httpContext = Post(new MemoryStream("{\"name\":\"Ada\"}"u8.ToArray()));
        httpContext.Request.Path = "/users";
        httpContext.Request.ContentType = "application/json";
        var body = new MemoryStream();
        httpContext.Response.Body = body;

        await SmithyAspNetCoreHost.DispatchAsync(
            SmithyServerRuntime.Default,
            httpContext,
            protocol,
            (input, _) => Task.FromResult(new CreateUserOutput(input.Name))
        );

        Assert.Equal(200, httpContext.Response.StatusCode);
        Assert.Equal("{\"name\":\"Ada\"}", Encoding.UTF8.GetString(body.ToArray()));
        Assert.Equal(body.Length, httpContext.Response.ContentLength);
    }

    [Fact]
    public async Task ASingleChunkBodyYieldsItsContentOnce()
    {
        var response = new SmithyHttpServerResponse
        {
            Body = SmithyHttpServerResponse.SingleChunk("abc"u8.ToArray()),
        };

        var chunks = new List<byte[]>();
        await foreach (var chunk in response.Body)
        {
            chunks.Add(chunk.ToArray());
        }

        Assert.True(response.TryGetSingleChunk(out var content));
        Assert.Equal("abc"u8.ToArray(), content.ToArray());
        Assert.Equal("abc"u8.ToArray(), Assert.Single(chunks));
        Assert.False(new SmithyHttpServerResponse().TryGetSingleChunk(out _));
    }

    private static DefaultHttpContext Post(Stream body)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/";
        httpContext.Request.Body = body;
        return httpContext;
    }

    private sealed class UnreadableStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new InvalidOperationException("The body was read.");

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
