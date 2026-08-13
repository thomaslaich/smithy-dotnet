using System.Text;
using System.Xml.Linq;
using NSmithy.Codecs.Xml;
using NSmithy.Core.Serde;
using NSmithy.Http;
using NSmithy.Protocols.Rest;

namespace NSmithy.Protocols.RestXml;

public sealed class RestXmlProtocol : IProtocol
{
    private static readonly IRestBodyCodecFactory BodyCodecFactory = new XmlRestBodyCodecFactory();

    /// <summary>
    /// Binds the protocol to a service, yielding per-operation protocols. REST derives each
    /// operation's binding from its <c>@http</c> trait, so the service schema is accepted for a
    /// uniform factory signature but not otherwise consulted. restXml uses raw string payloads and
    /// encodes errors in the XML body (no error-type header); server-side error serialization isn't
    /// wired up yet, so no error-type header is supplied.
    /// </summary>
    public IServiceProtocol ForService(ServiceSchema service) =>
        new RestServiceProtocol(
            _ => BodyCodecFactory,
            DeserializeErrorType,
            rawStringPayloads: true,
            errorTypeHeader: null
        );

    public static string? DeserializeErrorType(SmithyHttpClientResponse response)
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

    private sealed class XmlRestBodyCodecFactory : IRestBodyCodecFactory
    {
        public string ContentType => "application/xml";

        public string BlobContentType => "application/octet-stream";

        public ICodec<T> CodecFor<T>(Schema<T> schema) => XmlCodec.FromSchema(schema);

        public IProjectionCodec<T, TBuilder> CodecFor<T, TBuilder>(
            StructProjection<T, TBuilder> projection,
            bool materializeTopLevelDefaults
        ) => XmlCodec.FromProjection(projection, materializeTopLevelDefaults);
    }
}
