using NSmithy.Codecs;
using NSmithy.Codecs.Cbor;
using NSmithy.Http;

namespace NSmithy.Client.RpcV2Cbor;

public static class RpcV2CborClientProtocol
{
    public static T DeserializeBody<T>(ISmithyPayloadCodec codec, byte[] content)
    {
        return content.Length == 0 ? default! : codec.Deserialize<T>(content);
    }

    public static T DeserializeRequiredBody<T>(ISmithyPayloadCodec codec, byte[] content)
    {
        if (content.Length == 0)
        {
            throw new InvalidOperationException("Response body is required but was empty.");
        }

        return codec.Deserialize<T>(content);
    }

    public static bool HasResponse(SmithyHttpResponse response)
    {
        return response.Headers.TryGetValue("Smithy-Protocol", out var values)
            && values.Any(value => string.Equals(value, "rpc-v2-cbor", StringComparison.Ordinal));
    }

    public static void EnsureResponse(SmithyHttpResponse response)
    {
        if (!HasResponse(response))
        {
            throw new InvalidOperationException(
                "rpcv2Cbor response is missing the required Smithy-Protocol header."
            );
        }
    }

    public static string? DeserializeErrorType(byte[] content)
    {
        return SmithyCborPayloadCodec.DeserializeMember<string?>(content, "__type");
    }
}
