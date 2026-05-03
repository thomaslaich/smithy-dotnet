using NSmithy.Core.Serde;
using NSmithy.Http;

namespace NSmithy.Client.RpcV2Cbor;

public static class RpcV2CborClientProtocol
{
    public static T DeserializeBody<T>(ISmithyCodec codec, byte[] content)
        where T : IDeserializableShape<T> =>
        NSmithy.Protocols.RpcV2Cbor.RpcV2CborClientProtocol.DeserializeBody<T>(codec, content);

    public static T DeserializeRequiredBody<T>(ISmithyCodec codec, byte[] content)
        where T : IDeserializableShape<T> =>
        NSmithy.Protocols.RpcV2Cbor.RpcV2CborClientProtocol.DeserializeRequiredBody<T>(
            codec,
            content
        );

    public static bool HasResponse(SmithyHttpResponse response) =>
        NSmithy.Protocols.RpcV2Cbor.RpcV2CborClientProtocol.HasResponse(response);

    public static void EnsureResponse(SmithyHttpResponse response) =>
        NSmithy.Protocols.RpcV2Cbor.RpcV2CborClientProtocol.EnsureResponse(response);

    public static string? DeserializeErrorType(byte[] content) =>
        NSmithy.Protocols.RpcV2Cbor.RpcV2CborClientProtocol.DeserializeErrorType(content);
}
