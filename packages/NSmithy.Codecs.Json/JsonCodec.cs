using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Json;

public interface IJsonCodec<T> : ICodec<T> { }

public static partial class JsonCodec
{
    public static IJsonCodec<T> FromSchema<T>(Schema<T> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new CompiledJsonCodec<T>(schema);
    }

    public static IProjectionCodec<T> FromProjection<T>(
        StructProjection<T> projection,
        bool materializeTopLevelDefaults = true
    )
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new CompiledJsonProjectionCodec<T>(projection, materializeTopLevelDefaults);
    }
}
