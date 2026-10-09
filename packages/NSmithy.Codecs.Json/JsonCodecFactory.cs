using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Json;

/// <summary>Creates JSON codecs using one consistent wire read mode and member-name policy.</summary>
/// <param name="readMode">How strictly values are checked against their shapes when read.</param>
/// <param name="honorJsonNameTrait">Whether <c>@jsonName</c> renames a member on the wire.</param>
/// <param name="honorTimestampFormatTrait">
/// Whether <c>@timestampFormat</c> overrides the <c>epoch-seconds</c> default.
/// </param>
/// <param name="bigNumbersAsStrings">
/// Whether <c>bigInteger</c> and <c>bigDecimal</c> values travel as JSON strings rather than numbers.
/// </param>
public sealed class JsonCodecFactory(
    WireReadMode readMode = WireReadMode.Lenient,
    bool honorJsonNameTrait = true,
    bool honorTimestampFormatTrait = true,
    bool bigNumbersAsStrings = false
) : IProjectionCodecFactory
{
    public static JsonCodecFactory Default { get; } = new();

    public static JsonCodecFactory Strict { get; } = new(WireReadMode.Strict);

    public ICodec<T> FromSchema<T>(Schema<T> schema, CodecFactoryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var resolved = options ?? CodecFactoryOptions.Default;
        return new CompiledJsonCodec<T>(
            schema,
            resolved.MaterializeTopLevelDefaults,
            readMode,
            CreatePlans()
        );
    }

    public ICodec<T> FromMember<T>(
        Schema<T> target,
        IReadOnlyDictionary<ShapeId, Trait> memberTraits,
        CodecFactoryOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        var resolved = options ?? CodecFactoryOptions.Default;
        return new CompiledJsonCodec<T>(
            target,
            resolved.MaterializeTopLevelDefaults,
            readMode,
            CreatePlans(),
            memberTraits
        );
    }

    public IProjectionCodec<T, TBuilder> FromProjection<T, TBuilder>(
        StructProjection<T, TBuilder> projection,
        CodecFactoryOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(projection);
        var resolved = options ?? CodecFactoryOptions.Default;
        return new CompiledJsonProjectionCodec<T, TBuilder>(
            projection,
            resolved.MaterializeTopLevelDefaults,
            readMode,
            CreatePlans()
        );
    }

    internal JsonPlans CreatePlans() =>
        new(honorJsonNameTrait, honorTimestampFormatTrait, bigNumbersAsStrings);
}
