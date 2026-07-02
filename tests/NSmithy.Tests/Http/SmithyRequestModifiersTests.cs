using System.IO.Compression;
using System.Security.Cryptography;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Http;

namespace NSmithy.Tests.Http;

public sealed class SmithyRequestModifiersTests
{
    [Fact]
    public void CompileReturnsNullForOperationsWithoutRequestTraits()
    {
        Assert.Null(SmithyRequestModifiers.Compile(Operation()));
    }

    [Fact]
    public void CompiledTransformGzipsBodyAndSetsContentEncoding()
    {
        var transform = SmithyRequestModifiers.Compile(Operation(compressionEncoding: "gzip"));
        var request = Request("hello world"u8.ToArray());

        transform!(request);

        Assert.Equal(["gzip"], request.ContentHeaders["Content-Encoding"]);
        Assert.Equal(
            "hello world"u8.ToArray(),
            Gunzip(((SmithyHttpBody.Bytes)request.Body).Content)
        );
    }

    [Fact]
    public void CompiledTransformAppliesContentMd5()
    {
        var transform = SmithyRequestModifiers.Compile(Operation(checksumRequired: true));
        var body = "hello world"u8.ToArray();
        var request = Request(body);

        transform!(request);

#pragma warning disable CA5351
        Assert.Equal(
            [Convert.ToBase64String(MD5.HashData(body))],
            request.ContentHeaders["Content-MD5"]
        );
#pragma warning restore CA5351
    }

    [Fact]
    public void ChecksumCoversTheCompressedBody()
    {
        var transform = SmithyRequestModifiers.Compile(
            Operation(compressionEncoding: "gzip", checksumRequired: true)
        );
        var request = Request("hello world"u8.ToArray());

        transform!(request);

#pragma warning disable CA5351
        Assert.Equal(
            [Convert.ToBase64String(MD5.HashData(((SmithyHttpBody.Bytes)request.Body).Content))],
            request.ContentHeaders["Content-MD5"]
        );
#pragma warning restore CA5351
    }

    private static OperationSchema<string, string> Operation(
        string? compressionEncoding = null,
        bool checksumRequired = false
    )
    {
        List<Trait> traits = [];
        if (compressionEncoding is not null)
        {
            traits.Add(
                new Trait(
                    ShapeId.Parse("smithy.api#requestCompression"),
                    Document.From(
                        new Dictionary<string, Document>
                        {
                            ["encodings"] = Document.From([Document.From(compressionEncoding)]),
                        }
                    )
                )
            );
        }

        if (checksumRequired)
        {
            traits.Add(new Trait(ShapeId.Parse("smithy.api#httpChecksumRequired")));
        }

        return Schemas.Operation(
            ShapeId.Parse("example.weather#PutForecast"),
            Schemas.String,
            Schemas.String,
            traits
        );
    }

    private static SmithyHttpRequest Request(byte[] body) =>
        new(HttpMethod.Post, "/forecast") { Body = new SmithyHttpBody.Bytes(body) };

    private static byte[] Gunzip(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }
}
