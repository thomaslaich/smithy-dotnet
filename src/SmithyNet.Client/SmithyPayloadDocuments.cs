using System.Text;
using System.Text.Json;
using SmithyNet.Json;
using SmithyNet.Xml;

namespace SmithyNet.Client;

public static class SmithyPayloadDocuments
{
    public static byte[] SerializeMembers(
        ISmithyPayloadCodec codec,
        string rootName,
        IReadOnlyDictionary<string, object?> members
    )
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootName);
        ArgumentNullException.ThrowIfNull(members);

        return codec switch
        {
            SmithyXmlPayloadCodec => Encoding.UTF8.GetBytes(
                SmithyXmlSerializer.SerializeMembers(rootName, members)
            ),
            _ => codec.Serialize(members),
        };
    }

    public static T DeserializeMember<T>(ISmithyPayloadCodec codec, byte[] content, string name)
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return codec switch
        {
            SmithyJsonPayloadCodec => DeserializeJsonMember<T>(content, name),
            SmithyXmlPayloadCodec => SmithyXmlSerializer.DeserializeMember<T>(
                Encoding.UTF8.GetString(content),
                name
            ),
            SmithyCborPayloadCodec => SmithyCborPayloadCodec.DeserializeNamedMember<T>(content, name),
            _ => throw new NotSupportedException(
                $"Named document member deserialization is not supported by codec '{codec.GetType()}'."
            ),
        };
    }

    private static T DeserializeJsonMember<T>(byte[] content, string name)
    {
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
