using System.Text;
using System.Text.Json;

namespace NSmithy.Messaging.Redis;

internal static class RedisReplyEnvelope
{
    internal static byte[] Encode(string correlationId, byte[] payload)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteString("correlation_id", correlationId);
            json.WritePropertyName("data");
            json.WriteRawValue(payload);
            json.WriteEndObject();
        }
        return stream.ToArray();
    }

    internal static (string CorrelationId, byte[] Payload) Decode(byte[] bytes)
    {
        using var json = JsonDocument.Parse(bytes);
        var root = json.RootElement;
        return (
            root.GetProperty("correlation_id").GetString()
                ?? throw new JsonException("Missing reply correlation ID."),
            Encoding.UTF8.GetBytes(root.GetProperty("data").GetRawText())
        );
    }
}
