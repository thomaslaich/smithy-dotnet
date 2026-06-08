namespace NSmithy.Core.Serde;

// Codecs serialize Smithy shapes to and from the wire. The wire is always bytes;
// text formats (JSON, XML) encode to UTF-8 internally. A string view, when useful
// for debugging or tests, belongs in a format-specific convenience extension.
public interface ICodec<TValue>
{
    byte[] Serialize(TValue value);

    TValue Deserialize(byte[] payload);
}

public interface IProjectionCodec<TValue>
{
    byte[] Serialize(TValue value);

    void ReadInto(byte[] payload, object builder);
}
