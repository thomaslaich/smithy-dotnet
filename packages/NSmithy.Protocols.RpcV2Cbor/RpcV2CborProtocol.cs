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

    // Smithy 2.0 wraps `input: Unit` / `output: Unit` in synthetic structures that carry
    // this trait pointing back to the original `smithy.api#Unit` shape id.
    private static readonly ShapeId SyntheticOriginalShapeId = new("smithy.synthetic", "originalShapeId");
    private static readonly string UnitShapeIdString = "smithy.api#Unit";

    /// <summary>Returns true for synthetic unit-derived schemas that carry no members.</summary>
    private static bool IsUnitSchema(Schema schema) =>
        schema.HasTrait(SyntheticOriginalShapeId)
        && schema.GetTrait(SyntheticOriginalShapeId)?.Value.Kind == DocumentKind.String
        && schema.GetTrait(SyntheticOriginalShapeId)?.Value.AsString() == UnitShapeIdString;

    public static SmithyHttpRequest SerializeRequest<TInput, TOutput>(
        OperationSchema<TInput, TOutput> operation,
        TInput input,
        string requestUri
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(requestUri);

        var request = new SmithyHttpRequest(HttpMethod.Post, requestUri);
        request.Headers["Smithy-Protocol"] = ["rpc-v2-cbor"];
        request.Headers["Accept"] = [ContentType];

        if (typeof(TInput) != typeof(SmithyUnit) && !IsUnitSchema(operation.Input))
        {
            request.Content = CborCodec.FromSchema(operation.Input).Serialize(input);
            request.ContentType = ContentType;
        }

        return request;
    }

    public static TOutput DeserializeResponse<TInput, TOutput>(
        OperationSchema<TInput, TOutput> operation,
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

        return DeserializeBody(CborCodec.FromSchema(operation.Output), response.Content);
    }

    public static TError DeserializeError<TError>(
        Schema<TError> errorSchema,
        SmithyHttpResponse response
    )
    {
        ArgumentNullException.ThrowIfNull(errorSchema);
        ArgumentNullException.ThrowIfNull(response);

        return DeserializeRequiredBody(CborCodec.FromSchema(errorSchema), response.Content);
    }

    public static T DeserializeBody<T>(ICodec<T> codec, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(codec);
        return content.Length == 0 ? default! : codec.Deserialize(content);
    }

    public static T DeserializeRequiredBody<T>(ICodec<T> codec, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(codec);
        if (content.Length == 0)
        {
            throw new InvalidOperationException("Response body is required but was empty.");
        }

        return codec.Deserialize(content);
    }

    public static TInput DeserializeRequest<TInput, TOutput>(
        OperationSchema<TInput, TOutput> operation,
        SmithyHttpRequest request
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(request);

        if (typeof(TInput) == typeof(SmithyUnit) || IsUnitSchema(operation.Input))
            return default!;

        return DeserializeBody(CborCodec.FromSchema(operation.Input), request.Content ?? []);
    }

    public static SmithyHttpResponse SerializeResponse<TInput, TOutput>(
        OperationSchema<TInput, TOutput> operation,
        TOutput output
    )
    {
        ArgumentNullException.ThrowIfNull(operation);

        var responseHeaders = new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["Smithy-Protocol"] = ["rpc-v2-cbor"],
        };
        var emptyContentHeaders = new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase
        );

        if (typeof(TOutput) == typeof(SmithyUnit) || IsUnitSchema(operation.Output))
        {
            return new SmithyHttpResponse(
                System.Net.HttpStatusCode.OK,
                null,
                [],
                responseHeaders,
                emptyContentHeaders
            );
        }

        var body = CborCodec.FromSchema(operation.Output).Serialize(output);
        var contentHeaders = new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["Content-Type"] = [ContentType],
        };

        return new SmithyHttpResponse(
            System.Net.HttpStatusCode.OK,
            null,
            body,
            responseHeaders,
            contentHeaders
        );
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
