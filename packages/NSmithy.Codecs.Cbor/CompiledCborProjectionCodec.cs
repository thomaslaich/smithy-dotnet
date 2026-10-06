using System.Formats.Cbor;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Cbor;

internal sealed class CompiledCborProjectionCodec<T, TBuilder> : IProjectionCodec<T, TBuilder>
{
    private readonly IStructSchema<T, TBuilder> source;
    private readonly CborShapePlan plan;
    private readonly bool materializeTopLevelDefaults;

    public CompiledCborProjectionCodec(
        StructProjection<T, TBuilder> projection,
        bool materializeTopLevelDefaults
    )
    {
        source = projection.Source;
        this.materializeTopLevelDefaults = materializeTopLevelDefaults;
        plan = new CborPlans()
            .ForTarget((Schema)projection.Source)!
            .Project(name => projection.GetMember(name) is not null);
    }

    public byte[] Serialize(T value)
    {
        var writer = CborWriterCache.Rent();
        try
        {
            if (value is null)
            {
                writer.WriteNull();
            }
            else
            {
                writer.WriteStartMap(null);
                CborShapeSerializer.WriteMembers(
                    writer,
                    plan,
                    materializeTopLevelDefaults,
                    source,
                    value
                );
                writer.WriteEndMap();
            }

            return writer.Encode();
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
}
