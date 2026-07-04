using System.IO.Compression;
using System.Security.Cryptography;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Http;

/// <summary>
/// Request mutations driven by operation traits (<c>@requestCompression</c>,
/// <c>@httpChecksumRequired</c>). HTTP-body protocols compile them once per operation via
/// <see cref="Compile{TInput, TOutput}"/> and apply the result at the end of request
/// serialization; protocols with their own framing (gRPC) must handle these traits in their own
/// wire terms instead.
/// </summary>
public static class SmithyRequestModifiers
{
    private static readonly ShapeId RequestCompressionTraitId = ShapeId.Parse(
        "smithy.api#requestCompression"
    );
    private static readonly ShapeId HttpChecksumRequiredTraitId = ShapeId.Parse(
        "smithy.api#httpChecksumRequired"
    );

    /// <summary>
    /// Compiles the operation's request-mutating HTTP traits into a single transform, or null
    /// when the operation has none.
    /// </summary>
    public static Action<SmithyHttpRequest>? Compile<TInput, TOutput>(
        OperationSchema<TInput, TOutput> operation
    )
    {
        ArgumentNullException.ThrowIfNull(operation);

        var encoding = RequestCompressionEncoding(operation.GetTrait(RequestCompressionTraitId));
        var checksumRequired = operation.HasTrait(HttpChecksumRequiredTraitId);
        if (encoding is null && !checksumRequired)
        {
            return null;
        }

        return request =>
        {
            if (encoding is not null)
            {
                ApplyRequestCompression(request, encoding);
            }

            if (checksumRequired)
            {
                ApplyContentMd5(request);
            }
        };
    }

    public static bool HasRequestCompression<TInput, TOutput>(
        OperationSchema<TInput, TOutput> operation
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        return operation.HasTrait(RequestCompressionTraitId);
    }

    private static string? RequestCompressionEncoding(Trait? trait)
    {
        if (trait is not { } compression || compression.Value.Kind != DocumentKind.Object)
        {
            return null;
        }

        if (
            !compression.Value.AsObject().TryGetValue("encodings", out var encodings)
            || encodings.Kind != DocumentKind.Array
        )
        {
            return null;
        }

        var values = encodings.AsArray();
        return values.Count > 0 ? values[0].AsString() : null;
    }

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
