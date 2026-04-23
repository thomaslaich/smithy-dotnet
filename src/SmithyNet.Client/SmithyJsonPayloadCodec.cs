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

    public byte[] SerializeMembers(string rootName, IReadOnlyDictionary<string, object?> members)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootName);
        ArgumentNullException.ThrowIfNull(members);

        return Serialize(members);
    }

    public T Deserialize<T>(byte[] content)
    {
        return SmithyJsonSerializer.Deserialize<T>(Encoding.UTF8.GetString(content));
    }

    public T DeserializeMember<T>(byte[] content, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var json = Encoding.UTF8.GetString(content);
        if (string.IsNullOrWhiteSpace(json))
        {
            return default!;
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(name, out var value)
            ? SmithyJsonSerializer.Deserialize<T>(value.GetRawText())
            : default!;
    }
}
