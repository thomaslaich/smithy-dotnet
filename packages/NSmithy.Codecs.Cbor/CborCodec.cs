using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Cbor;

public interface ICborCodec<T> : ICodec<T> { }

public static class CborCodec
{
    /// <summary>
    /// Creates a codec for <paramref name="schema"/>. <paramref name="materializeTopLevelDefaults"/>
    /// controls whether the top-level structure writes members that carry a <c>@default</c> trait
    /// when they are null; nested structures always materialize their defaults. Client requests
    /// pass <c>false</c> (top-level defaults are skipped on the wire); server responses pass
    /// <c>true</c>.
    /// </summary>
    public static ICborCodec<T> FromSchema<T>(
        Schema<T> schema,
        bool materializeTopLevelDefaults = true
    )
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new CompiledCborCodec<T>(schema, materializeTopLevelDefaults);
    }

    public static IProjectionCodec<T, TBuilder> FromProjection<T, TBuilder>(
        StructProjection<T, TBuilder> projection,
        bool materializeTopLevelDefaults = true
    )
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new CompiledCborProjectionCodec<T, TBuilder>(
            projection,
            materializeTopLevelDefaults
        );
    }
}
