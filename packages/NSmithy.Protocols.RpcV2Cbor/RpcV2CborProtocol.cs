using System.Formats.Cbor;
using NSmithy.Codecs.Cbor;
using NSmithy.Protocols.RpcV2;

namespace NSmithy.Protocols.RpcV2Cbor;

/// <summary>The <c>smithy.protocols#rpcv2Cbor</c> protocol: RPC v2 with CBOR bodies.</summary>
public sealed class RpcV2CborProtocol()
    : RpcV2Protocol(CborCodecFactory.Default, "application/cbor", "rpc-v2-cbor", "rpcv2Cbor")
{
    protected override string? ReadErrorType(byte[] content)
    {
        if (content.Length == 0)
        {
            return null;
        }

        try
        {
            var reader = new CborReader(content, CborConformanceMode.Lax);
            if (reader.PeekState() != CborReaderState.StartMap)
            {
                return null;
            }

            reader.ReadStartMap();
            while (reader.PeekState() != CborReaderState.EndMap)
            {
                var key = reader.ReadTextString();
                if (string.Equals(key, "__type", StringComparison.Ordinal))
                {
                    return reader.ReadTextString();
                }

                reader.SkipValue();
            }

            return null;
        }
        catch (Exception exception)
            when (exception is CborContentException or InvalidOperationException)
        {
            return null;
        }
    }
}
