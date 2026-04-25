using System.Text;
using System.Text.Json;
using SmithyNet.Json;

namespace SmithyNet.Client;

public sealed class SmithyJsonPayloadCodec : ISmithyPayloadCodec
{
    public static SmithyJsonPayloadCodec Default { get; } = new();

    public string MediaType => "application/json";

    public byte[] Serialize<T>(T value)
    {
        return Encoding.UTF8.GetBytes(SmithyJsonSerializer.Serialize(value));
    }

    public T Deserialize<T>(byte[] content)
    {
        return SmithyJsonSerializer.Deserialize<T>(Encoding.UTF8.GetString(content));
    }
}
