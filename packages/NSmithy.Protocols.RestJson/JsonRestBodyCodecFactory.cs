using NSmithy.Codecs.Json;
using NSmithy.Core.Serde;
using NSmithy.Protocols.Rest;

namespace NSmithy.Protocols.RestJson;

/// <summary>
/// JSON body wire format shared by restJson1 and alloy simpleRestJson: it compiles JSON codecs and
/// reports the JSON content types. The protocols differ only in their string-payload policy and
/// error discriminator, which live on the protocol (not the format), so a single instance serves
/// both.
/// </summary>
internal sealed class JsonRestBodyCodecFactory : IRestBodyCodecFactory
{
    public static JsonRestBodyCodecFactory Instance { get; } = new();

    public string ContentType => "application/json";

    public string BlobContentType => "application/octet-stream";

    public ICodec<T> CodecFor<T>(Schema<T> schema) => JsonCodec.FromSchema(schema);

    public IProjectionCodec<T, TBuilder> CodecFor<T, TBuilder>(
        StructProjection<T, TBuilder> projection,
        bool materializeTopLevelDefaults
    ) => JsonCodec.FromProjection(projection, materializeTopLevelDefaults);
}
