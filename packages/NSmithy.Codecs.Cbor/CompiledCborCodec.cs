using System.Buffers;
using System.Formats.Cbor;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Cbor;

internal sealed class CompiledCborCodec<T>(
    Schema<T> schema,
    bool materializeTopLevelDefaults,
    KeyValuePair<string, string>? discriminator
) : ICodec<T>
{
    private readonly CborMemberPlan root = new CborPlans().ForRoot(schema, discriminator);

    public byte[] Serialize(T value)
    {
        var writer = CborWriterCache.Rent();
        try
        {
            Write(writer, value);
            return writer.Encode();
        }
        finally
        {
            CborWriterCache.Return(writer);
        }
    }

    public void Serialize(T value, IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var writer = CborWriterCache.Rent();
        try
        {
            Write(writer, value);
            destination.Advance(writer.Encode(destination.GetSpan(writer.BytesWritten)));
        }
        finally
        {
            CborWriterCache.Return(writer);
        }
    }

    private void Write(CborWriter writer, T value)
    {
        var serializer = new CborShapeSerializer(writer, root, materializeTopLevelDefaults);
        schema.Write(MemberIndex.Root, value, ref serializer);
    }

    public T Deserialize(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length == 0)
        {
            return default!;
        }

        var reader = new CborReader(payload, CborConformanceMode.Lax);
        var deserializer = new CborShapeDeserializer(reader, root);
        return schema.Read(ref deserializer);
    }
}
