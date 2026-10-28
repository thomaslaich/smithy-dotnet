using System.Buffers;
using System.Formats.Cbor;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Cbor;

internal sealed class CompiledCborProjectionCodec<T, TBuilder>(
    StructProjection<T, TBuilder> projection,
    bool materializeTopLevelDefaults
) : IProjectionCodec<T, TBuilder>
{
    private readonly StructSchema<T, TBuilder> source = projection.Source;
    private readonly CborShapePlan plan = new CborPlans()
        .ForTarget(projection.Source)!
        .Project(name => projection.GetMember(name) is not null);
    private readonly bool materializeTopLevelDefaults = materializeTopLevelDefaults;

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

    public void ReadInto(byte[] payload, TBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(builder);
        if (payload.Length == 0)
        {
            return;
        }

        var reader = new CborReader(payload, CborConformanceMode.Lax);
        if (reader.PeekState() != CborReaderState.StartMap)
        {
            throw new InvalidOperationException("Expected CBOR map for structure projection.");
        }

        CborShapeDeserializer.ReadMembers(reader, plan, source, builder);
    }

    private void Write(CborWriter writer, T value)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        writer.WriteStartMap(null);
        CborShapeSerializer.WriteMembers(writer, plan, materializeTopLevelDefaults, source, value);
        writer.WriteEndMap();
    }
}
