using NSmithy.Aws;
using NSmithy.Client;
using NSmithy.Http;

namespace NSmithy.Tests.Aws;

public sealed class GlacierInterceptorTests
{
    [Fact]
    public async Task UploadArchiveAppliesRequiredRequestCustomizations()
    {
        const int chunkSize = 1024 * 1024;
        var content = new byte[chunkSize + 1];
        Array.Fill(content, (byte)'a', 0, chunkSize);
        content[^1] = (byte)'b';
        var request = new SmithyHttpRequest(
            HttpMethod.Post,
            "https://glacier.us-east-1.amazonaws.com/vaults/archive/archives"
        )
        {
            Body = new SmithyHttpBody.Streaming(
                new MemoryStream(content, writable: false),
                content.Length
            ),
        };
        var context = new SmithyContext();
        context.Set(SmithyContextKeys.OperationName, "UploadArchive");

        var customized = await new GlacierInterceptor().OnBeforeSigningAsync(context, request);

        Assert.Same(request, customized);
        Assert.Equal(
            "https://glacier.us-east-1.amazonaws.com/-/vaults/archive/archives",
            request.RequestUri
        );
        Assert.Equal(["2012-06-01"], request.Headers["X-Amz-Glacier-Version"]);
        Assert.Equal(
            ["371264331be3a89bb42c4fea3770469e9094f6ce8c8244b9ac2beb9ffd80e621"],
            request.Headers["X-Amz-Content-Sha256"]
        );
        Assert.Equal(
            ["fab4f7d8265da2a9b95d1adcbd8ccada4947044a672b746c376ce39a82286ae0"],
            request.Headers["X-Amz-Sha256-Tree-Hash"]
        );

        var buffered = Assert.IsType<SmithyHttpBody.Streaming>(request.Body);
        using var drained = new MemoryStream();
        await buffered.Content.CopyToAsync(drained);
        Assert.Equal(content, drained.ToArray());
    }

    [Fact]
    public async Task NonUploadOperationOnlyAddsVersionHeader()
    {
        var request = new SmithyHttpRequest(HttpMethod.Get, "/vaults/archive");
        var context = new SmithyContext();
        context.Set(SmithyContextKeys.OperationName, "DescribeVault");

        await new GlacierInterceptor().OnBeforeSigningAsync(context, request);

        Assert.Equal("/-/vaults/archive", request.RequestUri);
        Assert.Equal(["2012-06-01"], request.Headers["X-Amz-Glacier-Version"]);
        Assert.DoesNotContain("X-Amz-Content-Sha256", request.Headers.Keys);
        Assert.DoesNotContain("X-Amz-Sha256-Tree-Hash", request.Headers.Keys);
    }
}
