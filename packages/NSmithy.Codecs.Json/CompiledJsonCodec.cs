using System.Buffers;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Json;

internal sealed class CompiledJsonCodec<T>(
    Schema<T> schema,
    bool materializeTopLevelDefaults,
    WireReadMode readMode,
    JsonPlans plans,
    IReadOnlyDictionary<ShapeId, Trait>? memberTraits = null,
    KeyValuePair<string, string>? discriminator = null
) : ICodec<T>
{
    private readonly JsonMemberPlan root = plans.ForRoot(schema, memberTraits, discriminator);

    // Size hint carried between calls: a codec instance serializes the same shape
    // repeatedly, so the previous payload size is a good guess at the next one and
    // usually avoids growing the scratch buffer at all.
    private int sizeHint = 256;

    public byte[] Serialize(T value)
    {
        var buffer = PooledByteBufferWriterCache.Rent(sizeHint);
        var writer = JsonWriterCache.Rent(buffer);
        try
        {
            var serializer = JsonShapeSerializer.ForRoot(writer, root, materializeTopLevelDefaults);
            schema.Write(MemberIndex.Root, value, ref serializer);
            writer.Flush();
            sizeHint = buffer.WrittenCount;
            return buffer.WrittenSpan.ToArray();
        }
        finally
        {
            JsonWriterCache.Return(writer);
            PooledByteBufferWriterCache.Return(buffer);
        }
    }

    public void Serialize(T value, IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var writer = JsonWriterCache.Rent(destination);
        try
        {
            var serializer = JsonShapeSerializer.ForRoot(writer, root, materializeTopLevelDefaults);
            schema.Write(MemberIndex.Root, value, ref serializer);
            writer.Flush();
        }
        finally
        {
            JsonWriterCache.Return(writer);
        }
    }

    public T Deserialize(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return JsonShapeDeserializer.ReadPayload(payload, schema, root, readMode);
    }
}

internal sealed class CompiledJsonProjectionCodec<T, TBuilder>(
    StructProjection<T, TBuilder> projection,
    bool materializeTopLevelDefaults,
    WireReadMode readMode,
    JsonPlans plans
) : IProjectionCodec<T, TBuilder>
{
    private readonly StructSchema<T, TBuilder> source = projection.Source;
    private readonly JsonShapePlan plan = plans
        .ForTarget(projection.Source)!
        .Project(name => projection.GetMember(name) is not null);

    private int sizeHint = 256;

    public byte[] Serialize(T value)
    {
        var buffer = PooledByteBufferWriterCache.Rent(sizeHint);
        var writer = JsonWriterCache.Rent(buffer);
        try
        {
            JsonShapeSerializer.WriteObject(
                writer,
                plan,
                materializeTopLevelDefaults,
                source,
                value
            );
            writer.Flush();
            sizeHint = buffer.WrittenCount;
            return buffer.WrittenSpan.ToArray();
        }
        finally
        {
            JsonWriterCache.Return(writer);
            PooledByteBufferWriterCache.Return(buffer);
        }
    }

    public void Serialize(T value, IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var writer = JsonWriterCache.Rent(destination);
        try
        {
            JsonShapeSerializer.WriteObject(
                writer,
                plan,
                materializeTopLevelDefaults,
                source,
                value
            );
            writer.Flush();
        }
        finally
        {
            JsonWriterCache.Return(writer);
        }
    }

    public void ReadInto(byte[] payload, TBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(builder);

        JsonShapeDeserializer.ReadPayloadMembers(payload, plan, source, builder, readMode);
    }
}
