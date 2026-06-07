using System.IO.Compression;
using System.Security.Cryptography;
using NSmithy.Codecs.Cbor;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Http;

namespace NSmithy.Protocols.RpcV2Cbor;

public static class RpcV2CborProtocol
{
    private const string ContentType = "application/cbor";

    public static SmithyHttpRequest SerializeRequest<TInput, TOutput>(
        FunctionalOperationSchema<TInput, TOutput> operation,
        TInput input,
        string requestUri
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(requestUri);

        var request = new SmithyHttpRequest(HttpMethod.Post, requestUri);
        request.Headers["Smithy-Protocol"] = ["rpc-v2-cbor"];
        request.Headers["Accept"] = [ContentType];

        if (typeof(TInput) != typeof(SmithyUnit))
        {
            request.Content = FunctionalCborCodec.FromSchema(operation.Input).Serialize(input);
            request.ContentType = ContentType;
        }

        return request;
    }

    public static TOutput DeserializeResponse<TInput, TOutput>(
        FunctionalOperationSchema<TInput, TOutput> operation,
        SmithyHttpResponse response
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(response);

        if (typeof(TOutput) == typeof(SmithyUnit))
        {
            EnsureResponse(response);
            return (TOutput)(object)SmithyUnit.Value;
        }

        return DeserializeRequiredBody(
            FunctionalCborCodec.FromSchema(operation.Output),
            response.Content
        );
    }

    public static TError DeserializeError<TError>(
        FunctionalSchema<TError> errorSchema,
        SmithyHttpResponse response
    )
    {
        ArgumentNullException.ThrowIfNull(errorSchema);
        ArgumentNullException.ThrowIfNull(response);

        return DeserializeRequiredBody(
            FunctionalCborCodec.FromSchema(errorSchema),
            response.Content
        );
    }

    public static T DeserializeBody<T>(IFunctionalCodec<T> codec, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(codec);
        return content.Length == 0 ? default! : codec.Deserialize(content);
    }

    public static T DeserializeRequiredBody<T>(IFunctionalCodec<T> codec, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(codec);
        if (content.Length == 0)
        {
            throw new InvalidOperationException("Response body is required but was empty.");
        }

        return codec.Deserialize(content);
    }

    public static bool HasResponse(SmithyHttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.Headers.TryGetValue("Smithy-Protocol", out var values)
            && values.Any(value => string.Equals(value, "rpc-v2-cbor", StringComparison.Ordinal));
    }

    public static void EnsureResponse(SmithyHttpResponse response)
    {
        if (!HasResponse(response))
        {
            throw new InvalidOperationException(
                "rpcv2Cbor response is missing the required Smithy-Protocol header."
            );
        }
    }

    public static string? DeserializeErrorType(SmithyHttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return DeserializeErrorType(response.Content);
    }

    public static string? DeserializeErrorType(byte[] content)
    {
        if (content.Length == 0)
            return null;
        try
        {
            var reader = new System.Formats.Cbor.CborReader(
                content,
                System.Formats.Cbor.CborConformanceMode.Lax
            );
            if (reader.PeekState() != System.Formats.Cbor.CborReaderState.StartMap)
                return null;
            reader.ReadStartMap();
            while (reader.PeekState() != System.Formats.Cbor.CborReaderState.EndMap)
            {
                var key = reader.ReadTextString();
                if (string.Equals(key, "__type", StringComparison.Ordinal))
                    return reader.ReadTextString();
                reader.SkipValue();
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public static void ApplyRequestCompression(SmithyHttpRequest request, string encoding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(encoding);

        if (request.Content is null)
        {
            return;
        }

        request.Content = encoding switch
        {
            "gzip" => CompressGzip(request.Content),
            _ => throw new NotSupportedException(
                $"Request compression encoding '{encoding}' is not supported."
            ),
        };

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

        if (request.Content is null)
        {
            return;
        }

        request.ContentHeaders["Content-MD5"] =
        [
            Convert.ToBase64String(MD5.HashData(request.Content)),
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
