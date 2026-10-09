using System.Text.Json;
using NSmithy.Codecs.Json;
using NSmithy.Core.Serde;
using NSmithy.Protocols.RpcV2;

namespace NSmithy.Protocols.RpcV2Json;

/// <summary>The <c>smithy.protocols#rpcv2Json</c> protocol: RPC v2 with JSON bodies.</summary>
public sealed class RpcV2JsonProtocol()
    : RpcV2Protocol(CodecFactory, ContentType, "rpc-v2-json", "rpcv2Json")
{
    private const string ContentType = "application/json";

    // rpcv2Json names properties after members, always writes timestamps as epoch seconds, and
    // carries bigInteger and bigDecimal as strings.
    private static readonly JsonCodecFactory CodecFactory = new(
        honorJsonNameTrait: false,
        honorTimestampFormatTrait: false,
        bigNumbersAsStrings: true
    );

    protected override Func<TError, byte[]> CompileErrorBody<TError>(
        IStructSchema<TError> errorSchema,
        string errorShapeId
    )
    {
        var memberWriters = new JsonMembersWriter<TError>(errorSchema, CodecFactory);
        return error =>
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteString("__type", errorShapeId);
                memberWriters.Write(writer, error);
                writer.WriteEndObject();
            }

            return buffer.ToArray();
        };
    }

    protected override string? ReadErrorType(byte[] content)
    {
        if (content.Length == 0)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            return
                document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("__type", out var type)
                && type.ValueKind == JsonValueKind.String
                ? type.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
