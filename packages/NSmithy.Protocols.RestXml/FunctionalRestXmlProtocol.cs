using NSmithy.Codecs.Xml;
using NSmithy.Core.Serde;
using NSmithy.Http;
using NSmithy.Protocols.Rest;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace NSmithy.Protocols.RestXml;

public static class FunctionalRestXmlProtocol
{
    private static readonly IFunctionalRestBodyFormat BodyFormat =
        new FunctionalRestXmlBodyFormat();

    public static SmithyHttpRequest SerializeRequest<TInput, TOutput>(
        FunctionalOperationSchema<TInput, TOutput> operation,
        TInput input
    ) =>
        FunctionalRestProtocol.SerializeRequest(
            RestOperationBinding.From(operation),
            input,
            BodyFormat
        );

    public static TInput DeserializeRequest<TInput, TOutput>(
        FunctionalOperationSchema<TInput, TOutput> operation,
        SmithyHttpRequest request
    ) =>
        FunctionalRestProtocol.DeserializeRequest(
            RestOperationBinding.From(operation),
            request,
            BodyFormat
        );

    public static SmithyHttpResponse SerializeResponse<TInput, TOutput>(
        FunctionalOperationSchema<TInput, TOutput> operation,
        TOutput output
    ) =>
        FunctionalRestProtocol.SerializeResponse(
            RestOperationBinding.From(operation),
            output,
            BodyFormat
        );

    public static TOutput DeserializeResponse<TInput, TOutput>(
        FunctionalOperationSchema<TInput, TOutput> operation,
        SmithyHttpResponse response
    ) =>
        FunctionalRestProtocol.DeserializeResponse(
            RestOperationBinding.From(operation),
            response,
            BodyFormat
        );

    public static string? DeserializeErrorType(SmithyHttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.Content.Length == 0)
        {
            return null;
        }

        var document = XDocument.Parse(Encoding.UTF8.GetString(response.Content));
        var root =
            document.Root
            ?? throw new InvalidOperationException(
                "Response body was missing an XML root element."
            );
        var errorRoot =
            string.Equals(root.Name.LocalName, "ErrorResponse", StringComparison.Ordinal)
                ? root.Elements().FirstOrDefault(element => element.Name.LocalName == "Error")
                    ?? root
                : root;
        return errorRoot.Elements().FirstOrDefault(element => element.Name.LocalName == "Code")
            ?.Value;
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

    private sealed class FunctionalRestXmlBodyFormat : IFunctionalRestBodyFormat
    {
        public string ContentType => "application/xml";

        public string BlobContentType => "application/octet-stream";

        public byte[] Serialize<T>(FunctionalSchema<T> schema, T value) =>
            FunctionalXmlCodec.FromSchema(schema).Serialize(value);

        public byte[] Serialize(FunctionalSchema schema, object value) =>
            SerializeObject((dynamic)schema, value);

        public T Deserialize<T>(FunctionalSchema<T> schema, byte[] content) =>
            FunctionalXmlCodec.FromSchema(schema).Deserialize(content);

        public byte[] Serialize<T>(
            FunctionalStructProjection<T> projection,
            T value,
            bool materializeTopLevelDefaults = true
        ) =>
            FunctionalXmlCodec.FromProjection(projection).Serialize(value);

        public void ReadInto<T>(
            FunctionalStructProjection<T> projection,
            byte[] content,
            object builder
        ) => FunctionalXmlCodec.FromProjection(projection).ReadInto(content, builder);

        private static byte[] SerializeObject<T>(FunctionalSchema<T> schema, object value) =>
            FunctionalXmlCodec.FromSchema(schema).Serialize((T)value);
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
}
