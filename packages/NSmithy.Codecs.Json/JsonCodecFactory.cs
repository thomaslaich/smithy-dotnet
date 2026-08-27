using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Json;

/// <summary>Creates JSON codecs using one consistent wire read mode.</summary>
public sealed class JsonCodecFactory(WireReadMode readMode = WireReadMode.Lenient)
    : IProjectionCodecFactory
{
    public static JsonCodecFactory Default { get; } = new();

    public static JsonCodecFactory Strict { get; } = new(WireReadMode.Strict);

    public ICodec<T> FromSchema<T>(Schema<T> schema, CodecFactoryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var resolved = options ?? CodecFactoryOptions.Default;
        return new CompiledJsonCodec<T>(schema, resolved.MaterializeTopLevelDefaults, readMode);
    }

    public ICodec<T> FromMember<T>(
        ITargetedMemberSchema<T> member,
        CodecFactoryOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(member);
        var resolved = options ?? CodecFactoryOptions.Default;
        return new CompiledJsonCodec<T>(
            member.TargetSchema,
            resolved.MaterializeTopLevelDefaults,
            readMode,
            member.MemberTraits
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
            readMode
        );
    }
}
