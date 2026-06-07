using NSmithy.Codecs.Cbor;
using NSmithy.Core;
using NSmithy.Core.Functional;
using NSmithy.Http;

namespace NSmithy.Protocols.RpcV2Cbor;

public static class FunctionalRpcV2CborProtocol
{
    private const string ContentType = "application/cbor";

    public static SmithyHttpRequest SerializeRequest<TInput, TOutput>(
        FunctionalOperationSchema<TInput, TOutput> operation,
        TInput input,
        string requestUri
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(requestUri);

        var request = new SmithyHttpRequest(System.Net.Http.HttpMethod.Post, requestUri);
        request.Headers["Smithy-Protocol"] = ["rpc-v2-cbor"];
        request.Headers["Accept"] = [ContentType];

        if (typeof(TInput) != typeof(SmithyUnit))
        {
            request.Content = FunctionalCborCodec.FromSchema(operation.Input).Serialize(input);
            request.ContentType = ContentType;
        }

        return request;
    }

    public static TOutput DeserializeResponse<TInput, TOutput>(
        FunctionalOperationSchema<TInput, TOutput> operation,
        SmithyHttpResponse response
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(response);

        if (typeof(TOutput) == typeof(SmithyUnit))
        {
            RpcV2CborProtocol.EnsureResponse(response);
            return (TOutput)(object)SmithyUnit.Value;
        }

        return RpcV2CborProtocol.DeserializeRequiredBody(
            FunctionalCborCodec.FromSchema(operation.Output),
            response.Content
        );
    }

    public static bool HasResponse(SmithyHttpResponse response) =>
        RpcV2CborProtocol.HasResponse(response);

    public static string? DeserializeErrorType(SmithyHttpResponse response) =>
        RpcV2CborProtocol.DeserializeErrorType(response.Content);
}
