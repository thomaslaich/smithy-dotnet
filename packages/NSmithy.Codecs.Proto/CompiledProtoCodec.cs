using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Proto;

internal sealed class CompiledProtoCodec<T> : ICodec<T>
{
    private readonly Schema<T> schema;
    private readonly ProtoMemberPlan root;
    private int sizeHint = 64;

    public CompiledProtoCodec(Schema<T> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        this.schema = schema;
        root = new ProtoPlans().ForRoot(schema);
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
            var serializer = new ProtoShapeSerializer(writer, root);
            schema.Write(MemberIndex.Root, value, ref serializer);
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
        var deserializer = new ProtoShapeDeserializer(
            payload,
            new ProtoOccurrence(0, payload.Length, WireType.Len),
            root
        );
        return schema.Read(ref deserializer);
    }
}
