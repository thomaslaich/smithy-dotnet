using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using NSmithy.Codecs.Json;
using NSmithy.Core.Serde;
using NSmithy.Http;
using NSmithy.Protocols.Rest;

namespace NSmithy.Protocols.RestJson;

public static class RestJsonProtocol
{
    private static readonly IFunctionalRestBodyFormat BodyFormat =
        new FunctionalRestJsonBodyFormat();

    public static SmithyHttpRequest SerializeRequest<TInput, TOutput>(
        FunctionalOperationSchema<TInput, TOutput> operation,
        TInput input
    ) =>
        RestProtocol.SerializeRequest(
            RestOperationBinding.From(operation),
            input,
            BodyFormat
        );

    public static TInput DeserializeRequest<TInput, TOutput>(
        FunctionalOperationSchema<TInput, TOutput> operation,
        SmithyHttpRequest request
    ) =>
        RestProtocol.DeserializeRequest(
            RestOperationBinding.From(operation),
            request,
            BodyFormat
        );

    public static SmithyHttpResponse SerializeResponse<TInput, TOutput>(
        FunctionalOperationSchema<TInput, TOutput> operation,
        TOutput output
    ) =>
        RestProtocol.SerializeResponse(
            RestOperationBinding.From(operation),
            output,
            BodyFormat
        );

    public static TOutput DeserializeResponse<TInput, TOutput>(
        FunctionalOperationSchema<TInput, TOutput> operation,
        SmithyHttpResponse response
    ) =>
        RestProtocol.DeserializeResponse(
            RestOperationBinding.From(operation),
            response,
            BodyFormat
        );

    public static string? DeserializeErrorType(SmithyHttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var headerValue =
            TryGetFirstHeaderValue(response.Headers, "X-Amzn-Errortype")
            ?? TryGetFirstHeaderValue(response.ContentHeaders, "X-Amzn-Errortype");
        if (!string.IsNullOrWhiteSpace(headerValue))
        {
            return NormalizeErrorType(headerValue);
        }

        if (response.Content.Length == 0)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(response.Content);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (
                document.RootElement.TryGetProperty("__type", out var dunderType)
                && dunderType.ValueKind == JsonValueKind.String
            )
            {
                return NormalizeErrorType(dunderType.GetString());
            }

            if (
                document.RootElement.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String
            )
            {
                return NormalizeErrorType(code.GetString());
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    public static TError DeserializeError<TError>(
        FunctionalSchema<TError> errorSchema,
        SmithyHttpResponse response
    ) => RestProtocol.DeserializeError(errorSchema, response, BodyFormat);

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

    private sealed class FunctionalRestJsonBodyFormat : IFunctionalRestBodyFormat
    {
        public string ContentType => "application/json";

        public string BlobContentType => "application/octet-stream";

        public byte[] Serialize<T>(FunctionalSchema<T> schema, T value) =>
            FunctionalJsonCodec.FromSchema(schema).Serialize(value);

        public byte[] Serialize(FunctionalSchema schema, object value) =>
            SerializeObject((dynamic)schema, value);

        public T Deserialize<T>(FunctionalSchema<T> schema, byte[] content) =>
            FunctionalJsonCodec.FromSchema(schema).Deserialize(content);

        public byte[] Serialize<T>(
            FunctionalStructProjection<T> projection,
            T value,
            bool materializeTopLevelDefaults = true
        ) =>
            FunctionalJsonCodec
                .FromProjection(projection, materializeTopLevelDefaults)
                .Serialize(value);

        public void ReadInto<T>(
            FunctionalStructProjection<T> projection,
            byte[] content,
            object builder
        ) => FunctionalJsonCodec.FromProjection(projection).ReadInto(content, builder);

        private static byte[] SerializeObject<T>(FunctionalSchema<T> schema, object value) =>
            FunctionalJsonCodec.FromSchema(schema).Serialize((T)value);
    }

    private static byte[] CompressGzip(byte[] content)
    {
        using var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(content, 0, content.Length);
        }

        return stream.ToArray();
    }

    private static string? TryGetFirstHeaderValue(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string name
    )
    {
        foreach (var header in headers)
        {
            if (
                string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)
                && header.Value.Count > 0
            )
            {
                return header.Value[0];
            }
        }

        return null;
    }

    private static string NormalizeErrorType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value;
        var colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            text = text[..colon];
        }

        var hash = text.LastIndexOf('#');
        if (hash >= 0)
        {
            text = text[(hash + 1)..];
        }

        return text;
    }
}
