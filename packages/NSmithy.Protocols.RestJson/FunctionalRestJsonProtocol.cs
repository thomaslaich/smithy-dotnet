using System.Text;
using NSmithy.Codecs.Json;
using NSmithy.Core.Functional;
using NSmithy.Http;
using NSmithy.Protocols.Rest;

namespace NSmithy.Protocols.RestJson;

public static class FunctionalRestJsonProtocol
{
    private static readonly IFunctionalRestBodyFormat BodyFormat =
        new FunctionalRestJsonBodyFormat();

    public static SmithyHttpRequest SerializeRequest<TInput, TOutput>(
        FunctionalOperationSchema<TInput, TOutput> operation,
        TInput input
    ) => FunctionalRestProtocol.SerializeRequest(operation, input, BodyFormat);

    public static TInput DeserializeRequest<TInput, TOutput>(
        FunctionalOperationSchema<TInput, TOutput> operation,
        SmithyHttpRequest request
    ) => FunctionalRestProtocol.DeserializeRequest(operation, request, BodyFormat);

    public static SmithyHttpResponse SerializeResponse<TInput, TOutput>(
        FunctionalOperationSchema<TInput, TOutput> operation,
        TOutput output
    ) => FunctionalRestProtocol.SerializeResponse(operation, output, BodyFormat);

    public static TOutput DeserializeResponse<TInput, TOutput>(
        FunctionalOperationSchema<TInput, TOutput> operation,
        SmithyHttpResponse response
    ) => FunctionalRestProtocol.DeserializeResponse(operation, response, BodyFormat);

    public static string? DeserializeErrorType(SmithyHttpResponse response) =>
        RestJsonProtocol.DeserializeErrorType(response);

    public static void ApplyRequestCompression(SmithyHttpRequest request, string encoding) =>
        RestJsonProtocol.ApplyRequestCompression(request, encoding);

    public static void ApplyContentMd5(SmithyHttpRequest request) =>
        RestJsonProtocol.ApplyContentMd5(request);

    private sealed class FunctionalRestJsonBodyFormat : IFunctionalRestBodyFormat
    {
        public string ContentType => "application/json";

        public string BlobContentType => "application/octet-stream";

        public byte[] Serialize<T>(FunctionalSchema<T> schema, T value)
        {
            var codec = FunctionalJsonCodec.FromSchema(schema);
            return Encoding.UTF8.GetBytes(codec.Serialize(value));
        }

        public T Deserialize<T>(FunctionalSchema<T> schema, byte[] content)
        {
            var codec = FunctionalJsonCodec.FromSchema(schema);
            return codec.Deserialize(Encoding.UTF8.GetString(content));
        }

        public byte[] Serialize<T>(FunctionalStructProjection<T> projection, T value)
        {
            var codec = FunctionalJsonCodec.FromProjection(projection);
            return Encoding.UTF8.GetBytes(codec.Serialize(value));
        }

        public void ReadInto<T>(
            FunctionalStructProjection<T> projection,
            byte[] content,
            object builder
        )
        {
            var codec = FunctionalJsonCodec.FromProjection(projection);
            codec.ReadInto(Encoding.UTF8.GetString(content), builder);
        }
    }
}
