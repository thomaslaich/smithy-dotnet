using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Json;

public interface IJsonCodec<T> : ICodec<T> { }

public static class JsonCodec
{
    public static IJsonCodec<T> FromSchema<T>(Schema<T> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new CompiledJsonCodec<T>(schema);
    }

    public static IProjectionCodec<T, TBuilder> FromProjection<T, TBuilder>(
        StructProjection<T, TBuilder> projection,
        bool materializeTopLevelDefaults = true
    )
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new CompiledJsonProjectionCodec<T, TBuilder>(
            projection,
            materializeTopLevelDefaults
        );
    }
}
