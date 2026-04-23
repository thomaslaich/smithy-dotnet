namespace SmithyNet.Client;

public interface ISmithyPayloadCodec
{
    string MediaType { get; }

    byte[] Serialize<T>(T value);

    byte[] SerializeMembers(string rootName, IReadOnlyDictionary<string, object?> members);

    T Deserialize<T>(byte[] content);

    T DeserializeMember<T>(byte[] content, string name);
}
