using System.Formats.Cbor;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Cbor;

internal sealed class CompiledCborCodec<T>(Schema<T> schema, bool materializeTopLevelDefaults)
    : ICodec<T>
{
    private readonly CborMemberPlan root = new CborPlans().ForRoot(schema);

    public byte[] Serialize(T value)
    {
        var writer = CborWriterCache.Rent();
        try
        {
            var serializer = new CborShapeSerializer(writer, root, materializeTopLevelDefaults);
            schema.Write(MemberIndex.Root, value, ref serializer);
            return writer.Encode();
        }
        finally
        {
            CborWriterCache.Return(writer);
        }
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
