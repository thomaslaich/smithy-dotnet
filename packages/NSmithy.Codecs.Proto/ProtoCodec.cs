using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Proto;

public interface IProtoCodec<T> : ICodec<T> { }

/// <summary>Creates schema-driven protobuf codecs for Smithy structures and unions.</summary>
public static class ProtoCodec
{
    public static IProtoCodec<T> FromSchema<T>(Schema<T> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new CompiledProtoCodec<T>(schema);
    }
}
