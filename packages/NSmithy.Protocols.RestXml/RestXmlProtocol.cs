using System.Text;
using System.Xml.Linq;
using NSmithy.Codecs.Xml;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Http;
using NSmithy.Protocols.Rest;

namespace NSmithy.Protocols.RestXml;

public sealed class RestXmlProtocol : IProtocol
{
    /// <summary>
    /// Binds the protocol to a service, yielding per-operation protocols. REST derives each
    /// operation's binding from its <c>@http</c> trait, so the service schema is accepted for a
    /// uniform factory signature but not otherwise consulted. restXml uses raw string payloads and
    /// encodes errors in the XML body (no error-type header); server-side error serialization isn't
    /// wired up yet, so no error-type header is supplied.
    /// </summary>
    public IServiceProtocol ForService(ServiceSchema service) =>
        new RestServiceProtocol(
            _ => new XmlRestBodyCodecFactory(service),
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
        private static readonly ShapeId XmlNamespaceId = ShapeId.Parse("smithy.api#xmlNamespace");

        private readonly XmlCodecFactory codecFactory;

        public XmlRestBodyCodecFactory(ServiceSchema service)
        {
            if (service.GetTrait(XmlNamespaceId) is not { HasValue: true } trait)
            {
                codecFactory = XmlCodecFactory.Default;
                return;
            }

            var value = trait.Value.AsObject();
            var defaultNamespaceUri = value["uri"].AsString();
            var defaultNamespacePrefix = value.TryGetValue("prefix", out var prefix)
                ? prefix.AsString()
                : null;
            codecFactory = new XmlCodecFactory(defaultNamespaceUri, defaultNamespacePrefix);
        }

        public string ContentType => "application/xml";

        public string BlobContentType => "application/octet-stream";

        public ICodec<T> FromSchema<T>(Schema<T> schema, CodecFactoryOptions? options = null) =>
            codecFactory.FromSchema(schema, options);

        public ICodec<T> FromMember<T>(
            ITypedTargetMemberSchema<T> member,
            CodecFactoryOptions? options = null
        ) => codecFactory.FromMember(member, options);

        public IProjectionCodec<T, TBuilder> FromProjection<T, TBuilder>(
            StructProjection<T, TBuilder> projection,
            CodecFactoryOptions? options = null
        ) => codecFactory.FromProjection(projection, options);

        public byte[] PrepareErrorBody(byte[] content)
        {
            var root = XElement.Parse(Encoding.UTF8.GetString(content));
            if (!string.Equals(root.Name.LocalName, "ErrorResponse", StringComparison.Ordinal))
                return content;

            var error = root.Elements()
                .FirstOrDefault(element =>
                    string.Equals(element.Name.LocalName, "Error", StringComparison.Ordinal)
                );
            return error is null
                ? content
                : Encoding.UTF8.GetBytes(error.ToString(SaveOptions.DisableFormatting));
        }
    }
}
