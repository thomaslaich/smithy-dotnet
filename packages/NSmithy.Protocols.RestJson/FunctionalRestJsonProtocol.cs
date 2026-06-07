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
        RestJsonProtocol.DeserializeErrorType(response);

    public static TError DeserializeError<TError>(
        FunctionalSchema<TError> errorSchema,
        SmithyHttpResponse response
    ) => FunctionalRestProtocol.DeserializeError(errorSchema, response, BodyFormat);

    public static void ApplyRequestCompression(SmithyHttpRequest request, string encoding) =>
        RestJsonProtocol.ApplyRequestCompression(request, encoding);

    public static void ApplyContentMd5(SmithyHttpRequest request) =>
        RestJsonProtocol.ApplyContentMd5(request);

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
        ) => FunctionalJsonCodec.FromProjection(projection, materializeTopLevelDefaults).Serialize(value);

        public void ReadInto<T>(
            FunctionalStructProjection<T> projection,
            byte[] content,
            object builder
        ) => FunctionalJsonCodec.FromProjection(projection).ReadInto(content, builder);

        private static byte[] SerializeObject<T>(FunctionalSchema<T> schema, object value) =>
            FunctionalJsonCodec.FromSchema(schema).Serialize((T)value);
    }
}
