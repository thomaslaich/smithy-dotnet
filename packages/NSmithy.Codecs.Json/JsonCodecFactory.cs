using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Json;

/// <summary>
/// Creates JSON codecs using one consistent member-name policy. Codecs read
/// <see cref="WireReadMode.Strict"/> unless <see cref="CodecFactoryOptions.ReadMode"/> says
/// otherwise.
/// </summary>
/// <param name="honorJsonNameTrait">Whether <c>@jsonName</c> renames a member on the wire.</param>
/// <param name="honorTimestampFormatTrait">
/// Whether <c>@timestampFormat</c> overrides the <c>epoch-seconds</c> default.
/// </param>
/// <param name="bigNumbersAsStrings">
/// Whether <c>bigInteger</c> and <c>bigDecimal</c> values travel as JSON strings rather than numbers.
/// </param>
public sealed class JsonCodecFactory(
    bool honorJsonNameTrait = true,
    bool honorTimestampFormatTrait = true,
    bool bigNumbersAsStrings = false
) : IProjectionCodecFactory
{
    public static JsonCodecFactory Default { get; } = new();

    public ICodec<T> FromSchema<T>(Schema<T> schema, CodecFactoryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var resolved = options ?? CodecFactoryOptions.Default;
        return new CompiledJsonCodec<T>(
            schema,
            resolved.MaterializeTopLevelDefaults,
            resolved.ReadMode ?? WireReadMode.Strict,
            CreatePlans(),
            discriminator: resolved.Discriminator
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
            resolved.ReadMode ?? WireReadMode.Strict,
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
            resolved.ReadMode ?? WireReadMode.Strict,
            CreatePlans()
        );
    }

    internal JsonPlans CreatePlans() =>
        new(honorJsonNameTrait, honorTimestampFormatTrait, bigNumbersAsStrings);
}
