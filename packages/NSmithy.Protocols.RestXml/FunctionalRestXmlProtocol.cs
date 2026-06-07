using NSmithy.Codecs.Xml;
using NSmithy.Core.Functional;
using NSmithy.Http;
using NSmithy.Protocols.Rest;

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

    public static string? DeserializeErrorType(SmithyHttpResponse response) =>
        RestXmlProtocol.DeserializeErrorCode(response.Content);

    public static void ApplyRequestCompression(SmithyHttpRequest request, string encoding) =>
        RestXmlProtocol.ApplyRequestCompression(request, encoding);

    public static void ApplyContentMd5(SmithyHttpRequest request) =>
        RestXmlProtocol.ApplyContentMd5(request);

    private sealed class FunctionalRestXmlBodyFormat : IFunctionalRestBodyFormat
    {
        public string ContentType => "application/xml";

        public string BlobContentType => "application/octet-stream";

        public byte[] Serialize<T>(FunctionalSchema<T> schema, T value) =>
            FunctionalXmlCodec.FromSchema(schema).Serialize(value);

        public T Deserialize<T>(FunctionalSchema<T> schema, byte[] content) =>
            FunctionalXmlCodec.FromSchema(schema).Deserialize(content);

        public byte[] Serialize<T>(FunctionalStructProjection<T> projection, T value) =>
            FunctionalXmlCodec.FromProjection(projection).Serialize(value);

        public void ReadInto<T>(
            FunctionalStructProjection<T> projection,
            byte[] content,
            object builder
        ) => FunctionalXmlCodec.FromProjection(projection).ReadInto(content, builder);
    }
}
