using NSmithy.Codecs.Json;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Protocols.Rest;

namespace NSmithy.Protocols.RestJson;

/// <summary>
/// JSON body wire format shared by restJson1 and alloy simpleRestJson: it compiles JSON codecs and
/// reports the JSON content types. The protocols differ only in their string-payload policy and
/// error discriminator, which live on the protocol (not the format), so a single instance serves
/// both.
/// </summary>
internal sealed class JsonRestBodyCodecFactory(WireReadMode readMode) : IRestBodyCodecFactory
{
    private static readonly JsonRestBodyCodecFactory Lenient = new(WireReadMode.Lenient);
    private static readonly JsonRestBodyCodecFactory Strict = new(WireReadMode.Strict);

    /// <summary>The instance whose codecs read by the given rules; see <see cref="WireReadMode"/>.</summary>
    public static IRestBodyCodecFactory For(WireReadMode readMode) =>
        readMode == WireReadMode.Strict ? Strict : Lenient;

    public string ContentType => "application/json";

    public string BlobContentType => "application/octet-stream";

    private readonly JsonCodecFactory codecFactory = new(readMode);

    public ICodec<T> FromSchema<T>(Schema<T> schema, CodecFactoryOptions? options = null) =>
        codecFactory.FromSchema(schema, options);

    public ICodec<T> FromMember<T>(
        ITargetedMemberSchema<T> member,
        CodecFactoryOptions? options = null
    ) => codecFactory.FromMember(member, options);

    public IProjectionCodec<T, TBuilder> FromProjection<T, TBuilder>(
        StructProjection<T, TBuilder> projection,
        CodecFactoryOptions? options = null
    ) => codecFactory.FromProjection(projection, options);
}
