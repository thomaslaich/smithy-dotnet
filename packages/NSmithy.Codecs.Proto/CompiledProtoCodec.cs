using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Proto;

internal sealed class CompiledProtoCodec<T> : IProtoCodec<T>
{
    private readonly IProtoMessageWriter<T> writer;
    private readonly IProtoMessageReader<T> reader;
    private int sizeHint = 64;

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

        var writer = ProtoWriterCache.Rent(sizeHint);
        try
        {
            this.writer.Write(writer, value);
            var result = writer.ToArray();
            sizeHint = result.Length;
            return result;
        }
        finally
        {
            ProtoWriterCache.Return(writer);
        }
    }

    public T Deserialize(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return reader.Read(payload);
    }
}
