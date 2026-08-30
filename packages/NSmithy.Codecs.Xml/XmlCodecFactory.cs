using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Xml;

/// <summary>
/// Creates XML codecs with an optional service-level namespace inherited by document roots.
/// </summary>
public sealed class XmlCodecFactory(
    string? defaultNamespaceUri = null,
    string? defaultNamespacePrefix = null
) : IProjectionCodecFactory
{
    public static XmlCodecFactory Default { get; } = new();

    public ICodec<T> FromSchema<T>(Schema<T> schema, CodecFactoryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var resolved = options ?? CodecFactoryOptions.Default;
        return new CompiledXmlCodec<T>(
            schema,
            resolved.MaterializeTopLevelDefaults,
            memberTraits: null,
            defaultNamespaceUri,
            defaultNamespacePrefix
        );
    }

    public ICodec<T> FromMember<T>(
        ITypedTargetMemberSchema<T> member,
        CodecFactoryOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(member);
        var resolved = options ?? CodecFactoryOptions.Default;
        return new CompiledXmlCodec<T>(
            member.TypedTarget,
            resolved.MaterializeTopLevelDefaults,
            member.MemberTraits,
            defaultNamespaceUri,
            defaultNamespacePrefix
        );
    }

    public IProjectionCodec<T, TBuilder> FromProjection<T, TBuilder>(
        StructProjection<T, TBuilder> projection,
        CodecFactoryOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(projection);
        var resolved = options ?? CodecFactoryOptions.Default;
        return new CompiledXmlProjectionCodec<T, TBuilder>(
            projection,
            resolved.MaterializeTopLevelDefaults,
            resolved.DefaultRootName,
            defaultNamespaceUri,
            defaultNamespacePrefix
        );
    }
}
