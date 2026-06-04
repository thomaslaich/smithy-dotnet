using System.Text;
using NSmithy.Codecs.Json;
using NSmithy.Core.Functional;
using NSmithy.Http;
using NSmithy.Protocols.Rest;

namespace NSmithy.Protocols.RestJson;

public static class FunctionalRestJsonProtocol
{
    private static readonly IFunctionalRestBodyCodecFactory BodyCodecFactory =
        new FunctionalRestJsonBodyCodecFactory();

    public static SmithyHttpRequest SerializeRequest<TInput, TOutput>(
        FunctionalOperationSchema<TInput, TOutput> operation,
        TInput input
    ) => FunctionalRestProtocol.SerializeRequest(operation, input, BodyCodecFactory);

    public static TInput DeserializeRequest<TInput, TOutput>(
        FunctionalOperationSchema<TInput, TOutput> operation,
        SmithyHttpRequest request
    ) => FunctionalRestProtocol.DeserializeRequest(operation, request, BodyCodecFactory);

    public static SmithyHttpResponse SerializeResponse<TInput, TOutput>(
        FunctionalOperationSchema<TInput, TOutput> operation,
        TOutput output
    ) => FunctionalRestProtocol.SerializeResponse(operation, output, BodyCodecFactory);

    public static TOutput DeserializeResponse<TInput, TOutput>(
        FunctionalOperationSchema<TInput, TOutput> operation,
        SmithyHttpResponse response
    ) => FunctionalRestProtocol.DeserializeResponse(operation, response, BodyCodecFactory);

    public static string? DeserializeErrorType(SmithyHttpResponse response) =>
        RestJsonProtocol.DeserializeErrorType(response);

    public static void ApplyRequestCompression(SmithyHttpRequest request, string encoding) =>
        RestJsonProtocol.ApplyRequestCompression(request, encoding);

    public static void ApplyContentMd5(SmithyHttpRequest request) =>
        RestJsonProtocol.ApplyContentMd5(request);

    private sealed class FunctionalRestJsonBodyCodecFactory : IFunctionalRestBodyCodecFactory
    {
        public string ContentType => "application/json";

        public string BlobContentType => "application/octet-stream";

        public IFunctionalRestBodyCodec FromSchema(FunctionalSchema schema) =>
            new FunctionalRestJsonBodyCodec(schema);

        IFunctionalObjectCodec<byte[]> IFunctionalCodecFactory<byte[]>.FromSchema(
            FunctionalSchema schema
        ) => FromSchema(schema);
    }

    private sealed class FunctionalRestJsonBodyCodec(FunctionalSchema schema)
        : IFunctionalRestBodyCodec
    {
        private readonly IFunctionalJsonObjectCodec codec = FunctionalJsonCodec.FromSchema(schema);

        public byte[] Serialize(object? value) => Encoding.UTF8.GetBytes(codec.Serialize(value));

        public object? Deserialize(byte[] content) =>
            codec.Deserialize(Encoding.UTF8.GetString(content));
    }
}
