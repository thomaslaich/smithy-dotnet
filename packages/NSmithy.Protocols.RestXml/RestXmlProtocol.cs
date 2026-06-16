using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using NSmithy.Codecs.Xml;
using NSmithy.Core.Serde;
using NSmithy.Http;
using NSmithy.Protocols.Rest;

namespace NSmithy.Protocols.RestXml;

public static class RestXmlProtocol
{
    private static readonly IRestBodyCodecFactory BodyCodecFactory = new XmlRestBodyCodecFactory();

    /// <summary>
    /// Binds the protocol to a service, yielding per-operation protocols. REST derives each
    /// operation's binding from its <c>@http</c> trait, so the service schema is accepted for a
    /// uniform factory signature but not otherwise consulted. restXml uses raw string payloads and
    /// encodes errors in the XML body (no error-type header); server-side error serialization isn't
    /// wired up yet, so no error-type header is supplied.
    /// </summary>
    public static IServiceProtocol ForService(ServiceSchema service) =>
        new RestServiceProtocol(
            BodyCodecFactory,
            DeserializeErrorType,
            rawStringPayloads: true,
            errorTypeHeader: null
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
        var errorRoot = string.Equals(
            root.Name.LocalName,
            "ErrorResponse",
            StringComparison.Ordinal
        )
            ? root.Elements().FirstOrDefault(element => element.Name.LocalName == "Error") ?? root
            : root;
        return errorRoot
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == "Code")
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

    private sealed class XmlRestBodyCodecFactory : IRestBodyCodecFactory
    {
        public string ContentType => "application/xml";

        public string BlobContentType => "application/octet-stream";

        public ICodec<T> CodecFor<T>(Schema<T> schema) => XmlCodec.FromSchema(schema);

        // XML serialization doesn't distinguish top-level default materialization.
        public IProjectionCodec<T> CodecFor<T>(
            StructProjection<T> projection,
            bool materializeTopLevelDefaults
        ) => XmlCodec.FromProjection(projection);
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
