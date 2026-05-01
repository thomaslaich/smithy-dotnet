namespace SmithyNet.Codecs;

public interface ISmithyPayloadCodec
{
    string MediaType { get; }

    byte[] Serialize<T>(T value);

    T Deserialize<T>(byte[] content);
}
