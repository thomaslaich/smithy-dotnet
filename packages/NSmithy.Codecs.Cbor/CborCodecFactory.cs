using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Cbor;

/// <summary>Creates schema and projection codecs for the Smithy RPCv2 CBOR representation.</summary>
public sealed class CborCodecFactory : IProjectionCodecFactory
{
    private CborCodecFactory() { }

    public static CborCodecFactory Default { get; } = new();

    public ICodec<T> FromSchema<T>(Schema<T> schema, CodecFactoryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var resolved = options ?? CodecFactoryOptions.Default;
        return new CompiledCborCodec<T>(
            schema,
            resolved.MaterializeTopLevelDefaults,
            resolved.Discriminator
        );
    }

    public IProjectionCodec<T, TBuilder> FromProjection<T, TBuilder>(
        StructProjection<T, TBuilder> projection,
        CodecFactoryOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(projection);
        var resolved = options ?? CodecFactoryOptions.Default;
        return new CompiledCborProjectionCodec<T, TBuilder>(
            projection,
            resolved.MaterializeTopLevelDefaults
        );
    }
}
