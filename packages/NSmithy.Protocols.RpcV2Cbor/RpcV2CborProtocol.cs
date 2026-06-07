using NSmithy.Core.Functional;
using NSmithy.Core.Serde;
using NSmithy.Http;

namespace NSmithy.Protocols.RpcV2Cbor;

public static class RpcV2CborProtocol
{
    public static T DeserializeBody<T>(IFunctionalCodec<T> codec, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(codec);
        return content.Length == 0 ? default! : codec.Deserialize(content);
    }

    public static T DeserializeRequiredBody<T>(IFunctionalCodec<T> codec, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(codec);
        if (content.Length == 0)
        {
            throw new InvalidOperationException("Response body is required but was empty.");
        }

        return codec.Deserialize(content);
    }

    public static T DeserializeBody<T>(ISmithyCodec codec, byte[] content)
        where T : IDeserializableShape<T>
    {
        return content.Length == 0 ? default! : codec.Deserialize<T>(content);
    }

    public static T DeserializeRequiredBody<T>(ISmithyCodec codec, byte[] content)
        where T : IDeserializableShape<T>
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
        if (content.Length == 0)
            return null;
        try
        {
            var reader = new System.Formats.Cbor.CborReader(
                content,
                System.Formats.Cbor.CborConformanceMode.Lax
            );
            if (reader.PeekState() != System.Formats.Cbor.CborReaderState.StartMap)
                return null;
            reader.ReadStartMap();
            while (reader.PeekState() != System.Formats.Cbor.CborReaderState.EndMap)
            {
                var key = reader.ReadTextString();
                if (string.Equals(key, "__type", StringComparison.Ordinal))
                    return reader.ReadTextString();
                reader.SkipValue();
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}
