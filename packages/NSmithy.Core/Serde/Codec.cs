namespace NSmithy.Core.Serde;

// Codecs serialize Smithy shapes to and from the wire. The wire is always bytes;
// text formats (JSON, XML) encode to UTF-8 internally. A string view, when useful
// for debugging or tests, belongs in a format-specific convenience extension.
public interface ICodec<TValue>
{
    byte[] Serialize(TValue value);

    TValue Deserialize(byte[] payload);
}

/// <summary>
/// Options that affect how a codec is created from a schema, targeted member, or projection.
/// </summary>
public sealed record CodecFactoryOptions
{
    public static CodecFactoryOptions Default { get; } = new();

    /// <summary>
    /// Whether modelled defaults on the top-level structure are written when their runtime value is
    /// null. Nested structures always materialize their defaults.
    /// </summary>
    public bool MaterializeTopLevelDefaults { get; init; } = true;

    /// <summary>
    /// Fallback document root name for a structure projection whose source schema does not declare
    /// one. Ignored by formats that do not represent named document roots.
    /// </summary>
    public string? DefaultRootName { get; init; }
}

/// <summary>Creates complete codecs from Smithy schemas.</summary>
public interface ICodecFactory
{
    ICodec<T> FromSchema<T>(Schema<T> schema, CodecFactoryOptions? options = null);

    /// <summary>
    /// Creates a codec for a member target while retaining traits declared on the member. The
    /// default is correct for formats whose representation is controlled entirely by the target
    /// schema; trait-sensitive factories can override it.
    /// </summary>
    ICodec<T> FromMember<T>(ITargetedMemberSchema<T> member, CodecFactoryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(member);
        return FromSchema(member.TargetSchema, options);
    }
}

/// <summary>
/// Creates complete codecs and codecs for projections of structure members. Formats that always
/// encode whole messages, such as Protocol Buffers, need only <see cref="ICodecFactory"/>.
/// </summary>
public interface IProjectionCodecFactory : ICodecFactory
{
    IProjectionCodec<T, TBuilder> FromProjection<T, TBuilder>(
        StructProjection<T, TBuilder> projection,
        CodecFactoryOptions? options = null
    );
}

public interface IProjectionCodec<TValue, in TBuilder>
{
    byte[] Serialize(TValue value);

    void ReadInto(byte[] payload, TBuilder builder);
}
