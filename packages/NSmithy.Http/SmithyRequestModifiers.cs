using System.IO.Compression;
using System.Security.Cryptography;

namespace NSmithy.Http;

/// <summary>
/// Protocol-agnostic request mutations applied by the generated client based on operation traits
/// (<c>@requestCompression</c>, <c>@httpChecksumRequired</c>). They operate purely on the request
/// bytes, so they are identical across wire protocols and live here rather than being duplicated
/// per protocol.
/// </summary>
public static class SmithyRequestModifiers
{
    public static void ApplyRequestCompression(SmithyHttpRequest request, string encoding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(encoding);

        if (ReferenceEquals(request.Body, SmithyHttpBody.Empty))
        {
            return;
        }

        if (request.Body is not SmithyHttpBody.Bytes bytes)
        {
            throw new InvalidOperationException(
                "Request compression for streaming bodies is not supported."
            );
        }

        request.Body = new SmithyHttpBody.Bytes(
            encoding switch
            {
                "gzip" => CompressGzip(bytes.Content),
                _ => throw new NotSupportedException(
                    $"Request compression encoding '{encoding}' is not supported."
                ),
            }
        );

        if (
            request.ContentHeaders.TryGetValue("Content-Encoding", out var values)
            && values.Count > 0
        )
        {
            request.ContentHeaders["Content-Encoding"] =
            [
                $"{string.Join(", ", values)}, {encoding}",
            ];
            return;
        }

        request.ContentHeaders["Content-Encoding"] = [encoding];
    }

#pragma warning disable CA5351
    public static void ApplyContentMd5(SmithyHttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ReferenceEquals(request.Body, SmithyHttpBody.Empty))
        {
            return;
        }

        if (request.Body is not SmithyHttpBody.Bytes bytes)
        {
            throw new InvalidOperationException(
                "Content-MD5 for streaming bodies is not supported."
            );
        }

        request.ContentHeaders["Content-MD5"] =
        [
            Convert.ToBase64String(MD5.HashData(bytes.Content)),
        ];
    }
#pragma warning restore CA5351

    private static byte[] CompressGzip(byte[] content)
    {
        using var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(content, 0, content.Length);
        }

        return stream.ToArray();
    }
}
