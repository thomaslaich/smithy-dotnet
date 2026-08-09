using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Proto;

internal sealed class CompiledProtoCodec<T> : IProtoCodec<T>
{
    private readonly IProtoMessageWriter<T> writer;
    private readonly IProtoMessageReader<T> reader;

    public CompiledProtoCodec(Schema<T> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        writer = ProtoWriterCompiler.Compile(schema);
        reader = ProtoReaderCompiler.Compile(schema);
    }

    public byte[] Serialize(T value)
    {
        if (value is null)
        {
            return [];
        }

        var writer = new ProtoWriter();
        this.writer.Write(writer, value);
        return writer.ToArray();
    }

    public T Deserialize(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return reader.Read(payload);
    }
}
