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
    private static readonly ShapeId SyntheticOriginalShapeId = new(
        "smithy.synthetic",
        "originalShapeId"
    );
    private static readonly string UnitShapeIdString = "smithy.api#Unit";

    /// <summary>Returns true for synthetic unit-derived schemas that carry no members.</summary>
    private static bool IsUnitSchema(Schema schema) =>
        schema.HasTrait(SyntheticOriginalShapeId)
        && schema.GetTrait(SyntheticOriginalShapeId)?.Value.Kind == DocumentKind.String
        && schema.GetTrait(SyntheticOriginalShapeId)?.Value.AsString() == UnitShapeIdString;

    /// <summary>
    /// Binds the protocol to a service, yielding per-operation protocols. The service schema
    /// supplies the service shape name used to derive each operation's request path.
    /// </summary>
    public static IServiceProtocol ForService(ServiceSchema service)
    {
        ArgumentNullException.ThrowIfNull(service);
        return new ServiceProtocol(service);
    }

    private sealed class ServiceProtocol(ServiceSchema service) : IServiceProtocol
    {
        public IOperationProtocol<TInput, TOutput> ForOperation<TInput, TOutput>(
            OperationSchema<TInput, TOutput> operation
        )
        {
            ArgumentNullException.ThrowIfNull(operation);
            return new OperationProtocol<TInput, TOutput>(service, operation);
        }
    }

    private sealed class OperationProtocol<TInput, TOutput> : IOperationProtocol<TInput, TOutput>
    {
        private readonly string requestUri;
        private readonly bool inputIsUnit;
        private readonly bool outputIsSmithyUnit;
        private readonly bool outputIsUnit;
        private readonly ICborCodec<TInput> requestCodec;
        private readonly ICborCodec<TOutput> responseCodec;

        public OperationProtocol(ServiceSchema service, OperationSchema<TInput, TOutput> operation)
        {
            // The rpcv2Cbor path is service-derived; the protocol owns this wire detail.
            requestUri = $"/service/{service.Id.Name}/operation/{operation.Id.Name}";
            inputIsUnit = typeof(TInput) == typeof(SmithyUnit) || IsUnitSchema(operation.Input);
            outputIsSmithyUnit = typeof(TOutput) == typeof(SmithyUnit);
            outputIsUnit = outputIsSmithyUnit || IsUnitSchema(operation.Output);

            // Built once per operation; the right materialize policy baked in per direction.
            requestCodec = CborCodec.FromSchema(
                operation.Input,
                materializeTopLevelDefaults: false
            );
            responseCodec = CborCodec.FromSchema(
                operation.Output,
                materializeTopLevelDefaults: true
            );
        }

        public SmithyHttpRequest SerializeRequest(TInput input)
        {
            var request = new SmithyHttpRequest(HttpMethod.Post, requestUri);
            request.Headers["Smithy-Protocol"] = ["rpc-v2-cbor"];
            request.Headers["Accept"] = [ContentType];
            if (!inputIsUnit)
            {
                request.Content = requestCodec.Serialize(input);
                request.ContentType = ContentType;
            }

            return request;
        }

        public TOutput DeserializeResponse(SmithyHttpResponse response)
        {
            ArgumentNullException.ThrowIfNull(response);
            if (outputIsSmithyUnit)
            {
                EnsureResponse(response);
                return (TOutput)(object)SmithyUnit.Value;
            }

            return response.Content.Length == 0
                ? default!
                : responseCodec.Deserialize(response.Content);
        }

        public TInput DeserializeRequest(SmithyHttpRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (inputIsUnit)
            {
                return default!;
            }

            var content = request.Content ?? [];
            return content.Length == 0 ? default! : requestCodec.Deserialize(content);
        }

        public SmithyHttpResponse SerializeResponse(TOutput output)
        {
            var responseHeaders = new Dictionary<string, IReadOnlyList<string>>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["Smithy-Protocol"] = ["rpc-v2-cbor"],
            };

            if (outputIsUnit)
            {
                return new SmithyHttpResponse(
                    System.Net.HttpStatusCode.OK,
                    null,
                    [],
                    responseHeaders,
                    new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                );
            }

            var contentHeaders = new Dictionary<string, IReadOnlyList<string>>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["Content-Type"] = [ContentType],
            };

            return new SmithyHttpResponse(
                System.Net.HttpStatusCode.OK,
                null,
                responseCodec.Serialize(output),
                responseHeaders,
                contentHeaders
            );
        }

        public bool IsErrorResponse(SmithyHttpResponse response) => (int)response.StatusCode >= 400;

        public string? GetErrorDiscriminator(SmithyHttpResponse response) =>
            HasResponse(response) ? DeserializeErrorType(response) : null;

        public TError DeserializeError<TError>(
            Schema<TError> errorSchema,
            SmithyHttpResponse response
        ) => RpcV2CborProtocol.DeserializeError(errorSchema, response);

        public SmithyHttpResponse SerializeError<TError>(
            Schema<TError> errorSchema,
            TError error,
            string errorShapeId,
            int statusCode
        ) => RpcV2CborProtocol.SerializeError(errorSchema, error, errorShapeId, statusCode);
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

    /// <summary>
    /// Serializes a modeled error into a rpcv2Cbor error response: a CBOR body carrying a
    /// <c>__type</c> discriminator (the absolute shape id) plus the error's members, with the
    /// supplied HTTP status code and the protocol header.
    /// </summary>
    public static SmithyHttpResponse SerializeError<TError>(
        Schema<TError> errorSchema,
        TError error,
        string errorShapeId,
        int statusCode
    )
    {
        ArgumentNullException.ThrowIfNull(errorSchema);
        ArgumentNullException.ThrowIfNull(errorShapeId);

        var body = CborCodec.SerializeError(errorSchema, error, errorShapeId);
        var responseHeaders = new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["Smithy-Protocol"] = ["rpc-v2-cbor"],
        };
        var contentHeaders = new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["Content-Type"] = [ContentType],
        };

        return new SmithyHttpResponse(
            (System.Net.HttpStatusCode)statusCode,
            null,
            body,
            responseHeaders,
            contentHeaders
        );
    }

    private static T DeserializeRequiredBody<T>(ICodec<T> codec, byte[] content)
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
