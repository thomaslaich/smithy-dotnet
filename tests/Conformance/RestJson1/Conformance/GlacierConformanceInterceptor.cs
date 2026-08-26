using System.Security.Cryptography;
using NSmithy.Client;
using NSmithy.Http;

namespace RestJson1.Conformance;

/// <summary>
/// Supplies the service-specific customizations required by the Glacier protocol fixtures. These
/// behaviors are not modeled as Smithy traits, so applications provide them through the public
/// interceptor seam rather than baking AWS service knowledge into the RestJson1 protocol.
/// </summary>
internal sealed class GlacierConformanceInterceptor : IClientInterceptor
{
    private const int TreeHashChunkSize = 1024 * 1024;

    public async ValueTask<SmithyHttpRequest> OnBeforeSigningAsync(
        SmithyContext context,
        SmithyHttpRequest request,
        CancellationToken cancellationToken = default
    )
    {
        request.Headers["X-Amz-Glacier-Version"] = ["2012-06-01"];

        var operation = context.Get(SmithyContextKeys.OperationName);
        if (operation == "UploadArchive")
            NormalizeEmptyAccountId(request);

        if (operation is "UploadArchive" or "UploadMultipartPart")
        {
            var body = await BufferBodyAsync(request, cancellationToken).ConfigureAwait(false);
            if (body.Length > 0)
            {
                request.Headers.TryAdd("X-Amz-Content-Sha256", [ToLowerHex(SHA256.HashData(body))]);
                request.Headers.TryAdd("X-Amz-Sha256-Tree-Hash", [ComputeTreeHash(body)]);
            }
        }

        return request;
    }

    private static async Task<byte[]> BufferBodyAsync(
        SmithyHttpRequest request,
        CancellationToken cancellationToken
    )
    {
        switch (request.Body)
        {
            case SmithyHttpBody.Bytes bytes:
                return bytes.Content;
            case SmithyHttpBody.Streaming streaming:
                using (var buffered = new MemoryStream())
                {
                    await streaming
                        .Content.CopyToAsync(buffered, cancellationToken)
                        .ConfigureAwait(false);
                    var content = buffered.ToArray();
                    request.Body = new SmithyHttpBody.Streaming(
                        new MemoryStream(content, writable: false),
                        content.Length
                    );
                    return content;
                }
            default:
                if (ReferenceEquals(request.Body, SmithyHttpBody.Empty))
                    return [];
                throw new NotSupportedException(
                    "Glacier checksum customization does not support event-stream bodies."
                );
        }
    }

    private static void NormalizeEmptyAccountId(SmithyHttpRequest request)
    {
        if (Uri.TryCreate(request.RequestUri, UriKind.Absolute, out var absolute))
        {
            var path = absolute.AbsolutePath;
            if (path.StartsWith("/vaults/", StringComparison.Ordinal))
            {
                var builder = new UriBuilder(absolute) { Path = "/-" + path };
                request.RequestUri = builder.Uri.AbsoluteUri;
            }
            else if (path.StartsWith("//vaults/", StringComparison.Ordinal))
            {
                var builder = new UriBuilder(absolute) { Path = "/-" + path[1..] };
                request.RequestUri = builder.Uri.AbsoluteUri;
            }
            return;
        }

        if (request.RequestUri.StartsWith("/vaults/", StringComparison.Ordinal))
            request.RequestUri = "/-" + request.RequestUri;
        else if (request.RequestUri.StartsWith("//vaults/", StringComparison.Ordinal))
            request.RequestUri = "/-" + request.RequestUri[1..];
    }

    private static string ComputeTreeHash(byte[] content)
    {
        var hashes = new List<byte[]>();
        for (var offset = 0; offset < content.Length; offset += TreeHashChunkSize)
        {
            var length = Math.Min(TreeHashChunkSize, content.Length - offset);
            hashes.Add(SHA256.HashData(content.AsSpan(offset, length)));
        }

        while (hashes.Count > 1)
        {
            var next = new List<byte[]>((hashes.Count + 1) / 2);
            for (var i = 0; i < hashes.Count; i += 2)
            {
                if (i + 1 == hashes.Count)
                {
                    next.Add(hashes[i]);
                    continue;
                }

                var pair = new byte[hashes[i].Length + hashes[i + 1].Length];
                hashes[i].CopyTo(pair, 0);
                hashes[i + 1].CopyTo(pair, hashes[i].Length);
                next.Add(SHA256.HashData(pair));
            }
            hashes = next;
        }

        return ToLowerHex(hashes[0]);
    }

    private static string ToLowerHex(byte[] hash) => Convert.ToHexString(hash).ToLowerInvariant();
}
