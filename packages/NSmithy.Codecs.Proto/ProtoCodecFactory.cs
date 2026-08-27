using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Proto;

/// <summary>Creates complete Protocol Buffer codecs from Smithy schemas.</summary>
public sealed class ProtoCodecFactory : ICodecFactory
{
    public static ProtoCodecFactory Default { get; } = new();

    private ProtoCodecFactory() { }

    public ICodec<T> FromSchema<T>(Schema<T> schema, CodecFactoryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new CompiledProtoCodec<T>(schema);
    }
}
