using System.Text;
using SmithyNet.Codecs;

namespace SmithyNet.Codecs.Xml;

public sealed class SmithyXmlPayloadCodec : ISmithyPayloadCodec
{
    public static SmithyXmlPayloadCodec Default { get; } = new();

    public string MediaType => "application/xml";

    public byte[] Serialize<T>(T value)
    {
        return Encoding.UTF8.GetBytes(SmithyXmlSerializer.Serialize(value));
    }

    public T Deserialize<T>(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return SmithyXmlSerializer.Deserialize<T>(Encoding.UTF8.GetString(content));
    }
}
